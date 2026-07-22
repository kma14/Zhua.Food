# zhua.food — item matching reference

How a per-store listing becomes a **item** so we can compare the *same item* across stores (plan
D9/D18). This is the platform's differentiator — and the trickiest judgement call in the system, because it scores
fuzzy text. When "same-product compare" looks wrong (two stores not merged, or two different items merged), start here.

Code: [`ItemMatcher`](../../src/Zhua.Application/Matching/ItemMatcher.cs) (Application use case, over `IMatchingRepository`) +
[`HeuristicItemMatchingPolicy`](../../src/Zhua.Domain/Services/HeuristicItemMatchingPolicy.cs) (the domain scoring rule) +
[`ProductNormalizer`](../../src/Zhua.Domain/Matching/ProductNormalizer.cs) (the pure text helpers).
Sibling docs: crawling/crawling → [crawling.md](crawling.md); the target redesign of the whole item layer →
[item-model.md](item-model.md); the deferred LLM matcher → [ai-matching.md](ai-matching.md).

---

## The three layers (recap)

```
Category   "Chicken Breast, Thighs & Tenders"   ← shared taxonomy tag        [D22]
   └─ Item   "Boneless Skinless Chicken Breast"  ← the same item across stores  [D9/D18]
         ├─ Product @ PAK'nSAVE Albany   $8.99   ← a real listing (has the price)
         └─ Product @ New World Metro     $9.99
```

The matcher's whole job is the **middle arrow**: deciding which `Product`s are the same real-world item and
linking them to one `Item`. `Product.ItemId` is **nullable** — matching is offline and
**never blocks crawling** (R3). Categorisation (the top arrow) is a separate step that runs right after — see
[CategoryMapper](../../src/Zhua.Application/Matching/CategoryMapper.cs) (D22).

## Where & when it runs

- **Offline, in the Worker** — never the Api (the Api only *reviews* matches, it doesn't compute them).
- The scheduled `CrawlingJob` runs it **after every crawl**, then runs the category mapper. Also on demand:
  `dotnet run --project src/Zhua.Worker -- match`.
- **Re-runnable from scratch every time** and converges to the same result (idempotent — see below).

## The anchor-priority cascade (D30)

Matching is a cascade ordered by **source data quality — Foodstuffs > Woolworths > FreshChoice**. A product only
becomes the *anchor* of a **new** item if it couldn't attach to one at a higher tier; a lower-quality source
*attaches* to a higher-tier item whenever it can. The effect is a near-invariant: **every active product ends up
with an `ItemId`.**

| Tier | Anchor `MatchKey` | Who joins | Yields |
|---|---|---|---|
| 1 | `foodstuffs:{sku}` | Foodstuffs branches by shared `productId` | multi-branch groups |
| 2 | *(attach to a Foodstuffs item)* | WW/FC by `brand+size+name` | cross-store compare |
| 3 | `woolworths:{sku}` | a WW product whose brand ∉ Foodstuffs-vocab; FC then attaches | WW+FC compare + WW singletons |
| 4 | `freshchoice:{sku}` | an FC product that attached to nothing above | FC singletons (1 store ⇒ always singleton) |

**The one deliberate exception to the invariant:** a product whose brand *is* a Foodstuffs brand but that didn't
attach at Tier 2 (a size-format / ambiguous miss) is **left unanchored** — anchoring it at Tier 3/4 would mint a
duplicate of the Foodstuffs item it belongs to and *split* the cross-store compare. These stay in the review queue
/ wait for better size-normalisation. Full analysis + the orphan decomposition: [orphan-matching.md](orphan-matching.md).

## Tier 1 — Foodstuffs (New World + PAK'nSAVE): free & exact

NW and PAK'nSAVE share one platform, so the *same* product has the *same* `productId` (= our `Sku`) at both
banners and across branches. So we just **group every Foodstuffs `Product` by `Sku`** → **one
`Item` per SKU**, and link every branch's listing to it. 100% reliable, fully automatic.

- The item's stable key: **`MatchKey = "foodstuffs:{sku}"`** (this is what makes re-runs idempotent).
- **`Name` + `Description` are seeded once on creation and never re-minted from store data** (D25 / phase 1) — so a
  store renaming its listing can't overwrite the owned item text. `Description` (= `Name` at seed time) is the
  owned grouping label. Brand/size/category are still refreshed each run (match filters, not display).
- Representative fields come from the group's **longest name** (most descriptive).
- `Category` (the denormalised leaf) = the listing's **finest store category** (Shelf > Aisle > Department).

This is why every Foodstuffs listing is *always* already grouped, and the review queue only ever holds Tier 2's
chains (Woolworths + FreshChoice).

## Tier 2 — Woolworths & FreshChoice: fuzzy, review-gated

Woolworths and FreshChoice share **no id and no GTIN** with Foodstuffs (D9 revised — see gotcha below), so their
listings are matched to a **Foodstuffs-derived item** in two stages:

1. **Hard filter on `brand + size`** — both must normalise and be equal (via `ProductNormalizer`). No brand or a
   loose size (e.g. `"kg"`, `"ea"` with no number) → **unmatchable**, skip. This cheaply rules out almost everything.
   **FreshChoice publishes no `RawBrand` at all (D26)** — see "Brand inference for FreshChoice" below for what
   feeds the hard filter for that chain.
2. **Score the survivors by name-token overlap** — `TokenOverlap(|A∩B| / min(|A|,|B|))` on significant name tokens
   (brand, stop-words, embedded sizes and bare numbers stripped out).

Then, per Tier 2 listing (the decision is [`HeuristicItemMatchingPolicy.Evaluate`](../../src/Zhua.Domain/Services/HeuristicItemMatchingPolicy.cs)):

| Outcome | Condition |
|---|---|
| **Auto-link** | best score **≥ 0.8** AND a clear single winner (margin > 0.001 over 2nd) |
| **Queue for review** | otherwise → the top ~3 candidates become `MatchCandidate` rows (Pending) |
| **Ignore** | best score **< 0.3** (`CandidateThreshold`) — too weak to even propose |

The knobs (`HeuristicItemMatchingPolicy`): `AutoLinkThreshold = 0.8`, `CandidateThreshold = 0.3`, clear-winner margin `0.001`.
The text rules (`ProductNormalizer`): brand/size normalisation, the stop-word list, the size-token regex. **Those are
the fragile bits** — tightening a threshold trades false-merges against more review-queue volume.

### Brand inference for FreshChoice (D29)

FreshChoice's SSR HTML carries no brand field, so `ItemMatcher` derives one before the hard filter above: try the
listing name's **leading two words**, then its **leading one word**, against the vocabulary of brands Tier 1
already knows (every normalised `Item.Brand` from the Foodstuffs pass — self-bootstrapping, no maintained list).
The first hit wins ("Meadow Fresh Yoghurt…" → `Meadow Fresh` beats matching just `Meadow`); no hit → `null`, same
as a Woolworths listing with no `RawBrand` — the hard filter skips it, same as any other unmatchable listing.

**Why this design over a plain "always take the first word" heuristic:** a wrong guess must be *free*. A dictionary
lookup can only ever produce a real Foodstuffs brand string, so a wrong guess (e.g. treating "Fennel" in "Fennel
Bulbs" as a brand) simply won't be in the `(brand,size)` index and falls through — same zero-candidate outcome as
if no guess had been made. A naive first-word-always heuristic can't make that guarantee (a coincidental token
match against an unrelated real brand risks a wrong candidate proposal). The candidate `Reason` string is tagged
`brand '{X}' inferred from name` whenever the brand wasn't literally on the source listing, so reviewers can see
when they're trusting a guess.

**Measured coverage (1,241 FreshChoice listings):** ~50% get a brand guess; of those, most still need the size to
line up before they can be scored at all — 306 auto-linked, 202 queued for review, 733 zero-candidate. Of the
zero-candidate set, ~81% never get a brand guess at all — mostly generic meat/produce cut names ("Beef Short Rib",
"Beef Roast Bolar") where the leading word is a category term, not a brand, so there's genuinely nothing to guess
from; the rest have a guessed brand but no Foodstuffs item shares its exact size — same shape as the Woolworths
zero-candidate cases below.

## Tier 3 — Woolworths-anchored items for what Foodstuffs doesn't carry (D30)

After Tier 2, a Woolworths product still unlinked **and** whose brand is **not** in the Foodstuffs vocabulary
(private label like `WW`/`Macro`, or a brand Foodstuffs simply doesn't stock) becomes its own item, keyed
`woolworths:{sku}`. Then **FreshChoice attaches to these WW anchors** by the *same* `brand+size+name` policy — but
the FC brand is inferred against the **Woolworths-anchor** brand vocabulary (which includes `WW`/`Macro`, absent
from the Foodstuffs vocab), not the Foodstuffs one. This is the only path to comparing **Woolworths-family private
label sold at both Woolworths and FreshChoice** — impossible while every item was Foodstuffs-anchored.

- The Foodstuffs-vocab guard is the correctness crux: a WW product whose brand *is* a Foodstuffs brand is **not**
  anchored here (it's a Tier-2 miss belonging to a Foodstuffs item — anchoring would duplicate + split the compare).
- `Name`/`Description` seeded once from the WW listing (D25); `Brand`/`Size` refreshed each run.

## Tier 4 — FreshChoice-anchored singletons (D30)

Whatever FreshChoice listing still attached to nothing becomes `freshchoice:{sku}` — always a **singleton** (one
FreshChoice store). Guard is **generic** (2026-07-22): if the name looks like a brand from *any* higher tier —
Foodstuffs **or** the Woolworths anchors — it's a suspected Tier-2/3b miss and is **left unanchored** (held for the
review queue / size normalisation), not minted as a duplicate. (Checking Foodstuffs brands alone let ~89 FreshChoice
listings that share a Woolworths private label — "WW"/"Macro"/"Essentials" — wrongly become singletons, splitting a
WW+FC compare.) The point of these singletons is the "every product
has an `ItemId`" invariant + readiness to **merge** when a real cross-store match later appears — **not** browsability
(a product reaches the shared category tree through its own `StoreCategory.CategoryId`, independent of matching).

**Measured on the live catalogue (2026-07-22):** Tier 3 minted **1,956** Woolworths-anchored items, **61** of which
gained a FreshChoice listing → genuine 2-store price compare that didn't exist before (e.g. Alpine Cheese Colby
$13.90 WW vs $14.90 FC). Tier 4 minted **529** FreshChoice singletons. The invariant's deliberate exception —
Woolworths products with a Foodstuffs brand that missed — is **861** listings, left for the review queue / size
normalisation. Item categories for the new anchors come through the existing store-category name mapping (~26% for
Woolworths); the rest stay `Uncategorized` until curated (does not affect browsability).

## Idempotency — why re-running is safe

Three mechanisms (in [`ItemMatcher`](../../src/Zhua.Application/Matching/ItemMatcher.cs)):

1. **Upsert items by `MatchKey`** — it loads every item, keys the ones with a `MatchKey`
   (`foodstuffs:` / `woolworths:` / `freshchoice:` / `manual:`) into a dictionary, and reuses them, so re-runs
   don't duplicate. Merge tombstones are resolved to their survivor here (see Merge below), so a merged-away key
   relinks to the survivor instead of recreating the item.
2. **Honour human decisions** — every `Approved` candidate is re-applied (sets the link); every `Rejected` pair is
   never re-proposed; a listing that's already linked (by DB `ItemId` or a link made earlier this run) is skipped.
3. **Drop resolved candidates** — at the end, Pending candidates whose product has since been linked are deleted.

## Human review (the queue → the Api)

Tier 2's ambiguous cases land in `MatchCandidate` (Pending). The **Api** exposes the review actions (the only writes
it makes) — full contract in [../api.md](../api.md#admin--match-review-d18). The three reviewer outcomes:

| Situation | Action | Effect on the matcher |
|---|---|---|
| A candidate is correct | `approve` | recorded as `Approved` → re-applied every run |
| None fit, but it's another existing item | `link-item` | sets the link directly |
| None fit, genuinely new | `create-item` | makes a new item (stamped `manual:` key) + links it |
| Two items are the same product | `merge` | repoints to the survivor; the tombstone resolves on re-run |
| Wrong pair | `reject` | recorded as `Rejected` → never re-proposed |

## Merge — correcting two items into one (rework phase 4)

When the matcher splits one real product into two items (e.g. two Foodstuffs SKUs that are the same thing, or a
hand-made item that duplicates an existing one), an admin **merges** them: `POST /items/{id}/merge { intoId }`
([ItemService.MergeAsync](../../src/Zhua.Infrastructure/Services/ItemService.cs)). It repoints the source's products +
candidates to the survivor, then leaves the source as a **redirect tombstone** (`Item.MergedIntoId` set) — *not* a
hard delete. The tombstone is deliberate: a deleted Foodstuffs item would be **recreated** by Tier 1 on the next run
(it regroups by `Sku` unconditionally). Keeping the row lets the matcher resolve the merged-away `MatchKey` to
the survivor instead:

- The matcher loads every item and **resolves `MatchKey → survivor`** through the `MergedIntoId` chain, so Tier 1
  links a tombstoned SKU's products to the survivor and never resurrects the tombstone.
- Tier 2's `(brand,size)` index and the item counts skip tombstones.
- `PATCH /products/{id}` refuses to link to a merged-away item (it would be undone next run).
- **Price history needs no special handling** — snapshots key on `ProductId`, so they follow the moved product.

Merge is also the **reverse-edge repair for the D30 cascade**: if Foodstuffs later stocks a product that a
Woolworths anchor already represents, both items exist for the same product — an admin merges the Woolworths item
into the Foodstuffs one (higher tier wins). This isn't automatic today; it's the documented manual step.

Merge is idempotent (re-merging into the same survivor is a no-op) and rejects self-merge / redirect cycles.

## Manual link/create vs. the matcher

`create-item` now stamps a stable **`MatchKey = "manual:{guid}"`** (so the hand-made item is visible to the upsert
**and** the `(brand,size)` index — later Woolworths products can auto-attach to it, and a re-run never orphans it).
Remaining edge:

- **Woolworths listings (what the queue actually contains): safe.** Tier 2 skips already-linked products and the
  matcher never nulls a link, so a manual link/create **survives**.
- **Foodstuffs listings: a manual `link-item` would still be overwritten.** Tier 1 unconditionally regroups
  Foodstuffs by `Sku`, so hand-linking a *Foodstuffs* listing to a different item doesn't stick. In practice
  the UI only acts on the Woolworths queue, so this stays a narrow edge — to genuinely combine two Foodstuffs items,
  use **merge** (above), not a manual link.

## Gotchas

- **No GTIN bridge (D9 revised).** The original plan was GTIN-first, but **Foodstuffs exposes no barcode**, so a
  GTIN can't bridge Woolworths/FreshChoice↔Foodstuffs. The bridge is `brand + size + name` (D18). We still capture
  Woolworths' GTIN at crawl time for future use.
- **Fresh/unbranded produce won't group** — no brand/size to filter on, so loose items (whole chickens,
  bulk veg) stay unmatched and are compared by category + `$/kg` instead.
- **A zero-candidate listing is usually correct, not a bug.** A breakdown of Woolworths' zero-candidate set
  (2026-07-20, 2,452 listings) found ~83% genuinely have no possible match: loose/weight-sold (9%), Woolworths'
  own private label (Woolworths/Macro — Foodstuffs' equivalent is Pams/Value, a different brand string, 19%), or
  a brand Foodstuffs simply doesn't stock (53%). The remaining ~17% share a real Foodstuffs brand but not its
  exact normalised size — the one bucket worth revisiting if size-normalisation is ever loosened.
- **The cascade is Foodstuffs > Woolworths > FreshChoice (D30, above)** — before it, every item was born from a
  Foodstuffs SKU, so a product sold only at Woolworths/FreshChoice was structurally unanchored. Now Tier 3/4 anchor
  those on Woolworths (then FreshChoice), so nearly every active product has an item. The remaining itemless set is
  the deliberate exception (Foodstuffs-brand Tier-2 misses). Category-browse never depended on items anyway (a
  product reaches the tree via `StoreCategory.CategoryId`); the fuller orphan analysis is [orphan-matching.md](orphan-matching.md).
- **Cross-store category coverage is partial** — the category mapper maps Foodstuffs by identity (100%)
  but other banners by exact name (Woolworths ~26%); that's a *categorisation* gap, separate from product matching.
  It's also why Tier-3/4 anchor items are mostly `Uncategorized` (their products carry no Foodstuffs category).

## Tests

`ItemMatcherTests` and `ProductNormalizerTests` in `tests/Zhua.Crawling.Tests` (EF InMemory + pure unit tests)
cover the tiers, the thresholds, idempotency, the normalisation rules, (D29) FreshChoice's brand inference, and
(D30) the Woolworths/FreshChoice anchor cascade + its Foodstuffs-brand guard.

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`, to the minute), then 🧑‍⚖️ if user-instructed.

- **2026-07-21 10:30** — 🧑‍⚖️ *(Kevin: "Freshchoice 这个要补上" — the front-end's matching-coverage report,
  verified first: every number checked out against the live DB except one unreproducible "81 missing size"
  figure)* **FreshChoice brand inference (D29) built** — see "Brand inference for FreshChoice" above. Folded into
  the existing Tier 2 loop (Woolworths ∪ FreshChoice) rather than a separate tier, since the only difference is
  where the brand string comes from. Also fixed a mislabelled `MatchRunResult.AutoLinked`: it was
  `CountLinkedProductsAsync` — a DB-wide, all-time, all-stores count masquerading as "linked this run" (the report
  flagged this too) — now a before/after diff scoped to the run's active-store product set.
- **2026-07-21 11:15** — 🧑‍⚖️ *(Kevin: "好" — approved after asking about a spotted bug)* **Ampersand edge case
  fixed.** `InferBrandFromName` split on whitespace and capped at 2 words, so a 3-word brand containing "&" (e.g.
  "Beak & Sons") truncated to the meaningless "Beak &" and never matched. Now tries 3/2/1 leading words
  (longest-first) and extends past a trailing lone "&" instead of counting it as a significant word. Verified on
  the live catalog: 293→306 auto-linked, 762→733 zero-candidate.
- **2026-07-22 — 🧑‍⚖️ (Kevin)** **Anchor-priority cascade (D30) built** (Foodstuffs > Woolworths > FreshChoice —
  see "The anchor-priority cascade" + Tier 3/4 above). Extends the Tier-2 loop with two anchor tiers so nearly
  every active product gets an item; guarded so Foodstuffs-brand Tier-2 misses never mint duplicates. Design +
  the four-bucket orphan decomposition it came out of: [orphan-matching.md](orphan-matching.md). Live result:
  +1,956 Woolworths-anchored (61 real WW+FC 2-store groups), +529 FreshChoice singletons, 861 guarded-out. Along
  the way we established (verified in code) that **category-browse does not depend on items** — a product reaches
  the shared tree via `StoreCategory.CategoryId` — so the singletons are justified by the "every product has an
  `ItemId`" invariant + future merges, not browsability. Category label for the new anchors left on the free
  ~26% name-mapping (option A); manual curation deferred.
- **2026-07-22 — 🧑‍⚖️ (Kevin: "捎带着 generic guard")** **Tier-4 guard made generic.** It checked only the
  Foodstuffs brand vocab, so a FreshChoice product whose brand is a *Woolworths-anchor* private label ("WW"/
  "Macro"/"Essentials") that missed Tier 3b still minted a `freshchoice:` singleton — splitting a WW+FC compare.
  Now checks `foodstuffsBrands ∪ wwBrands`. **Caveat — going-forward only:** `MatchKey` identity is stable, so a
  re-run skips already-linked products (`Linked(fc)`) and does **not** re-evaluate the **89** pre-existing
  singletons this would now hold (≈36 of them would attach to a WW anchor → real 2-store compares). Realising them
  needs a one-time reclaim (un-link + delete those singletons so the next run re-cascades them) — deferred pending
  Kevin's call, since it deletes items. Tracked as tech debt.

---

*Keep this in sync with `ItemMatcher` + `ProductNormalizer` — the thresholds and text rules drift as we tune them.*
