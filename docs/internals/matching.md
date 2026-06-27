# zhua.food — canonical matching reference

How a per-store listing becomes a **canonical product** so we can compare the *same item* across stores (plan
D9/D18). This is the platform's differentiator — and the trickiest judgement call in the system, because it scores
fuzzy text. When "same-product compare" looks wrong (two stores not merged, or two different items merged), start here.

Code: [`CanonicalMatcher`](../../src/Zhua.Infrastructure/Matching/CanonicalMatcher.cs) +
[`ProductNormalizer`](../../src/Zhua.Application/Matching/ProductNormalizer.cs) (the pure text helpers).
Sibling docs: crawling/ingestion → [crawling.md](crawling.md); the target redesign of the whole canonical layer →
[canonical-model.md](canonical-model.md); the deferred LLM matcher → [ai-matching.md](ai-matching.md).

---

## The three layers (recap)

```
CanonicalCategory   "Chicken Breast, Thighs & Tenders"   ← shared taxonomy tag        [D22]
   └─ CanonicalProduct   "Boneless Skinless Chicken Breast"  ← the same item across stores  [D9/D18]
         ├─ StoreProduct @ PAK'nSAVE Albany   $8.99   ← a real listing (has the price)
         └─ StoreProduct @ New World Metro     $9.99
```

The matcher's whole job is the **middle arrow**: deciding which `StoreProduct`s are the same real-world item and
linking them to one `CanonicalProduct`. `StoreProduct.CanonicalProductId` is **nullable** — matching is offline and
**never blocks ingestion** (R3). Categorisation (the top arrow) is a separate step that runs right after — see
[CanonicalCategoryMapper](../../src/Zhua.Infrastructure/Matching/CanonicalCategoryMapper.cs) (D22).

## Where & when it runs

- **Offline, in the Worker** — never the Api (the Api only *reviews* matches, it doesn't compute them).
- The scheduled `IngestionJob` runs it **after every crawl**, then runs the category mapper. Also on demand:
  `dotnet run --project src/Zhua.Worker -- match`.
- **Re-runnable from scratch every time** and converges to the same result (idempotent — see below).

## Tier 1 — Foodstuffs (New World + PAK'nSAVE): free & exact

NW and PAK'nSAVE share one platform, so the *same* product has the *same* `productId` (= our `SourceSku`) at both
banners and across branches. So we just **group every Foodstuffs `StoreProduct` by `SourceSku`** → **one
`CanonicalProduct` per SKU**, and link every branch's listing to it. 100% reliable, fully automatic.

- The canonical's stable key: **`MatchKey = "foodstuffs:{sku}"`** (this is what makes re-runs idempotent).
- **`Name` + `Description` are seeded once on creation and never re-minted from store data** (D25 / phase 1) — so a
  store renaming its listing can't overwrite the owned canonical text. `Description` (= `Name` at seed time) is the
  owned grouping label. Brand/size/category are still refreshed each run (match filters, not display).
- Representative fields come from the group's **longest name** (most descriptive).
- `Category` (the denormalised leaf) = the listing's **finest store category** (Shelf > Aisle > Department).

This is why every Foodstuffs listing is *always* already canonicalised, and the review queue is **Woolworths-only**.

## Tier 2 — Woolworths: fuzzy, review-gated

Woolworths shares **no id and no GTIN** with Foodstuffs (D9 revised — see gotcha below), so a Woolworths listing is
matched to a **Foodstuffs-derived canonical** in two stages:

1. **Hard filter on `brand + size`** — both must normalise and be equal (via `ProductNormalizer`). No brand or a
   loose size (e.g. `"kg"`, `"ea"` with no number) → **unmatchable**, skip. This cheaply rules out almost everything.
2. **Score the survivors by name-token overlap** — `TokenOverlap(|A∩B| / min(|A|,|B|))` on significant name tokens
   (brand, stop-words, embedded sizes and bare numbers stripped out).

Then, per Woolworths listing ([CanonicalMatcher.cs](../../src/Zhua.Infrastructure/Matching/CanonicalMatcher.cs#L88)):

| Outcome | Condition |
|---|---|
| **Auto-link** | best score **≥ 0.8** AND a clear single winner (margin > 0.001 over 2nd) |
| **Queue for review** | otherwise → the top ~3 candidates become `MatchCandidate` rows (Pending) |
| **Ignore** | best score **< 0.3** (`CandidateThreshold`) — too weak to even propose |

The knobs (`CanonicalMatcher`): `AutoLinkThreshold = 0.8`, `CandidateThreshold = 0.3`, clear-winner margin `0.001`.
The text rules (`ProductNormalizer`): brand/size normalisation, the stop-word list, the size-token regex. **Those are
the fragile bits** — tightening a threshold trades false-merges against more review-queue volume.

## Idempotency — why re-running is safe

Three mechanisms ([lines 36–86](../../src/Zhua.Infrastructure/Matching/CanonicalMatcher.cs#L36)):

1. **Upsert canonicals by `MatchKey`** — it loads `CanonicalProducts WHERE MatchKey != null` into a dictionary and
   reuses them, so re-runs don't duplicate. *(Canonicals with a null `MatchKey` are invisible to this — see the
   limitation below.)*
2. **Honour human decisions** — every `Approved` candidate is re-applied (sets the link); every `Rejected` pair is
   never re-proposed; a Woolworths listing that's already linked is skipped.
3. **Drop resolved candidates** — at the end, Pending candidates whose product has since been linked are deleted.

## Human review (the queue → the Api)

Tier 2's ambiguous cases land in `MatchCandidate` (Pending). The **Api** exposes the review actions (the only writes
it makes) — full contract in [../api.md](../api.md#admin--match-review-d18). The three reviewer outcomes:

| Situation | Action | Effect on the matcher |
|---|---|---|
| A candidate is correct | `approve` | recorded as `Approved` → re-applied every run |
| None fit, but it's another existing canonical | `link-canonical` | sets the link directly |
| None fit, genuinely new | `create-canonical` | makes a new canonical + links it |
| Wrong pair | `reject` | recorded as `Rejected` → never re-proposed |

## ⚠️ Known limitation — manual link/create vs. the matcher

`create-canonical` makes a `CanonicalProduct` with **no `MatchKey`**, and `link-canonical` sets a link without an
`Approved` record. Consequences on the next match run:

- **Woolworths listings (what the queue actually contains): safe.** Tier 2 skips already-linked products and the
  matcher never nulls a link, so a manual link/create **survives**. *(Caveat: a hand-made canonical isn't in the
  `(brand,size)` index — `MatchKey != null` only — so the matcher won't auto-attach **other** products to it later.)*
- **Foodstuffs listings: would be overwritten.** Tier 1 unconditionally regroups Foodstuffs by `SourceSku` and
  overwrites the link, orphaning the hand-made canonical. In practice the UI only acts on the Woolworths queue, so
  this is a narrow edge — but calling these endpoints on a Foodstuffs listing is unsafe today.

**Cheap hardening when needed:** stamp created canonicals with `MatchKey = "manual:{guid}"` (so they're visible to
the upsert and the index), and record manual links the same way `approve` does (or skip linked products in Tier 1
too). Low priority until the review UI is actually exercised.

## Gotchas

- **No GTIN bridge (D9 revised).** The original plan was GTIN-first, but **Foodstuffs exposes no barcode**, so a
  GTIN can't bridge Woolworths↔Foodstuffs. The bridge is `brand + size + name` (D18). We still capture Woolworths'
  GTIN at crawl time for future use.
- **Fresh/unbranded produce won't canonicalise** — no brand/size to filter on, so loose items (whole chickens,
  bulk veg) stay unmatched and are compared by category + `$/kg` instead.
- **Cross-store category coverage is partial** — the canonical category mapper maps Foodstuffs by identity (100%)
  but other banners by exact name (Woolworths ~26%); that's a *categorisation* gap, separate from product matching.

## Tests

`CanonicalMatcherTests` and `ProductNormalizerTests` in `tests/Zhua.Ingestion.Tests` (EF InMemory + pure unit tests)
cover the tiers, the thresholds, idempotency, and the normalisation rules.

---

*Keep this in sync with `CanonicalMatcher` + `ProductNormalizer` — the thresholds and text rules drift as we tune them.*
