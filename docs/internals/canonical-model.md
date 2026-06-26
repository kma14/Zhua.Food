# zhua.food — the canonical layer: purpose, rules & target design

Design note (not all implemented yet — see [Current vs target](#current-vs-target)). It settles *what* canonical
products and categories are for, *who* may see them, and *how* search and matching should work as we onboard more
supermarkets. Sibling deep-dives: [matching.md](matching.md) (the matcher), [crawling.md](crawling.md) (ingestion).

## TL;DR — the principles

1. **Store product = truth.** Real name, price, image, per store. The thing we search and the thing we display.
2. **Canonical product = an internal join key**: "we *think* these store listings are the same item." It is
   **never shown to a shopper and never appears in the shopper-facing API**. It carries an internal name/description
   used only as *matching features* (and, later, AI input).
3. **Canonical category = an owned, curated vocabulary** ("Chicken Breast", "Milk", "Eggs"). Common-sense words a
   shopper understands. **This is the one place curation happens**, and it *is* user-facing.
4. **Search store products, group by canonical** — search the real text; use the canonical only to de-duplicate.
5. **Matching is additive, identity-stable, non-destructive, and reviewable** — link/unlink/merge/split move
   *pointers* only; they never touch store listings, and human decisions survive every re-run and new store.

## Why each layer exists

The app answers *"where is X cheapest."* But there is no "X" in raw data — each store names, SKUs, and categorises
the same item differently (`woolworths nz beef eye fillet grass fed` vs `Pams Beef Eye Fillet`). Two unifications
are needed, for two different audiences:

- **CanonicalProduct** unifies *for the machine*: it's the identity that lets us compute "cheapest across stores",
  price history, and "on special anywhere." Without it you can only sort a category by `$/kg`, which mixes whole
  chicken with breast with drumsticks — meaningless, and exactly what kiwiprice does (CLAUDE.md §Prior art). It's
  the moat. But it's a *machine* construct, so it stays behind the scenes.
- **CanonicalCategory** unifies *for the shopper*: each store's aisle tree is different, so to let someone browse
  "Chicken Breast" and see every store's chicken breast, you need one shared taxonomy. Categories are small,
  stable, and intuitive — worth hand-owning, and meant to be seen.

## The rules

### R1 — Never display the canonical *name* as a product title
A product's **title** in any shopper view is a **real store name** (a representative store's `RawName`), never the
synthesized canonical anchor name. An invented title with no store behind it makes the user ask *"where is this
from?"*. The compare view shows every store's own name; list/search rows show a representative real name **with its
store context** ("Pams Beef Eye Fillet — cheapest at PAK'nSAVE $29.99").

### R2 — Expose the canonical's `id` + `description`, but not its anchor `name`
The canonical is internal, but two of its fields *are* useful to the front-end and are exposed:
- **`id`** — the grouping key / drill-in handle (when N store products collapse into one row, the UI needs the id).
- **`description`** — a short, honest **grouping label**: *"we think these 6 are the same thing: <description>"*.
  This is fine precisely because it's framed as our opinion (`we think`), not as the product's title (R1).

What stays hidden is the internal **anchor `name`** (the matcher/AI matching target) and other match internals. So:
the row's *title* is a real store name; the canonical `id` + `description` ride alongside as the group key and the
"why these are merged" explanation. *(Admin/review surfaces may show everything — the reviewer is not a shopper.)*

### R3 — Canonical category is the curated, owned, user-facing set
We define and maintain our own category vocabulary and tree. It is the **only** curation surface (rename/add/delete
categories). Products are not curated — you don't curate what you don't show.

### R4 — Store products are immutable truth; the canonical is an editable overlay
Linking/merging/splitting only moves pointers (`StoreProduct.CanonicalProductId`). It never edits a store listing.
The canonical is the *one* thing allowed to be wrong and get corrected, precisely because it's an opinion ("we
think"), not a fact (the store's listing).

### R5 — Matching is additive, stable, and reviewable
Re-runs and new-store onboarding **attach** listings to existing canonicals; they never destroy/recreate identities
or discard human-approved links. Wrong guesses are fixed via review, non-destructively.

## Canonical product internals

The canonical still needs attributes — but as **matching features, not display values**:

| Field | Exposed to shopper API? | Purpose |
|---|---|---|
| `Id` | **yes** | stable identity — the group key / drill-in handle; what links, history, URLs hang off |
| `Description` *(new)* | **yes** | dual-purpose: a human-readable **grouping label** ("we think these are: X") **and** a match signal (good LLM fodder) |
| internal `Name` (anchor) | **no** | the *match target* — clean, stable text the matcher/AI compares a store listing against |
| `Brand`, `Size`, `CanonicalCategoryId` | no | structured match filters |
| `MatchKey` | no | idempotent upsert key for the matcher |

The internal name/description should be a **stable controlled anchor** the matcher *searches against* — not a value
re-minted from store data each run. (Today it's the latter; see the gap below.) This is the reconciliation of the
two earlier instincts: we **do** want an owned, stable canonical name — but *for matching*, not for display.

## Search: store-first, grouped by canonical

> Search `StoreProduct.RawName`/`RawBrand` → collapse hits sharing a `CanonicalProductId` into one row → unmatched
> hits stand alone (a group of one).

Why store-first:
- **Coverage** — every listing is searchable, including those not yet linked to a canonical (no blind spots).
- **Recall** — you match the *real* text, so *"grass fed"* finds the item even when only the Woolworths listing
  says it and the canonical's anchor doesn't.

Each result row carries: a representative real name (cheapest store's) as the **title**, the **canonical `id`** (group
key) and its **`description`** (the "we think these are: X" grouping label, R2), cheapest price, and store count. One
canonical → one row; an unmatched listing → its own row.

**Drill-in id caveat:** a matched row drills in by its *canonical id*; an unmatched row only has a *store-product
id* (and no canonical `description`). The search response must carry enough to drill into both (the canonical `id`
when present + the representative store-product id), and the compare view must handle a "group of one."

## Matcher direction (additive, AI-assisted later)

Target shape (see [matching.md](matching.md) for today's implementation):
- A **stable set of canonical anchors**; matching = *find the best existing anchor and link*, or *propose a new
  anchor* (pending review). Never overwrite an anchor from store data.
- **Operations** the editable overlay implies: `link` / `unlink` / `merge` ("these two canonicals are one") /
  `split` ("this group swept in a different product"). Pointer moves only.
- **AI assist**: an LLM scores "does this listing belong to this canonical?" using the anchor name/description +
  brand/size/category — replacing/augmenting today's token-overlap heuristic, especially across dissimilar store
  wordings and new banners.

## Current vs target

| Area | Today | Target (this note) |
|---|---|---|
| Search | queries `CanonicalProduct` names ([ProductEndpoints](../../src/Zhua.Api/Endpoints/ProductEndpoints.cs)) | queries `StoreProduct` text, groups by canonical |
| Shopper API & the canonical | exposes the canonical **name** as the title (`ProductSummary.name`, `ProductComparison.name`, `CategoryProduct.product`) | title = real store name; expose canonical **`id` + `description`** (group key + "we think these are: X" label) — but **not** the anchor name |
| Canonical name source | re-minted from Foodstuffs every match run ([CanonicalMatcher:53](../../src/Zhua.Infrastructure/Matching/CanonicalMatcher.cs#L53)) | stable internal anchor; matcher links, never overwrites |
| Curation surface | none (names derived) | category CRUD only |
| Matcher | regenerative (Tier 1 rebuilds canonicals each run) | additive search-and-link; merge/split; AI-assisted |
| Category vocabulary | seeded from Foodstuffs taxonomy | owned/curated set |

> ⚠️ Several of these are **breaking API changes** for the front-end (Codex). Sequence and coordinate them — don't
> rip canonical fields out from under the live UI. Admin/review endpoints keep showing canonical internals.

## Open decisions

- Drill-in shape: return the **canonical `id`** for matched groups + the representative **store-product id** for
  singletons (and how the compare endpoint accepts either).
- How the `description` is generated — curated, seeded-then-frozen from the first source, or LLM-written — and
  whether the anchor `name` is a separate field or just reuses `description`.
- Merge/split endpoint design + what happens to history when two canonicals merge.
- When AI matching comes in, and how its proposals flow through the existing review queue.

---

*This is a target/direction note. Keep it in step with [matching.md](matching.md) as the matcher evolves toward it.*
