# zhua.food — orphan matching (research, deferred)

> **Status: RESEARCH — nothing built.** This studies the listings the matcher leaves **unmatched** (no `ItemId`)
> — the "orphans" — decomposes them by *why*, and asks what each bucket actually needs (which is **not** all "more
> AI"). Read [matching.md](matching.md) (how the matcher works today), [ai-matching.md](ai-matching.md) (the LLM
> plan for the *review queue*), [item-model.md](item-model.md) (why items exist, D25), and TD-5 in
> [tech-debt.md](tech-debt.md) (browsability of unmatched listings) first. **No decision is recorded here** — the
> "Open decisions" section is what needs Kevin's call before any of this is built.

## The headline finding: the item universe is Foodstuffs-shaped

Every item is born from a Foodstuffs SKU. Tier 1 creates one item per shared `productId`; Tier 2 only ever
*attaches* a Woolworths/FreshChoice listing to a **Foodstuffs-anchored** item. Measured on the live DB
(2026-07-21): **4192 / 4192 active items are `foodstuffs:`-keyed — zero are anchored on a Woolworths or
FreshChoice product.**

Consequence: **any product not sold at Foodstuffs has no item to attach to and is structurally unmatchable**, no
matter how good the matcher gets. That includes Woolworths private label (WW/Macro), FreshChoice's Woolworths-supplied
lines, and any brand only the Woolworths-NZ family carries. The matcher can't be blamed for these — there is
literally no anchor to link them to. This is an **architecture** gap, not a scoring gap, and it gates most of what
follows. (It's consistent with D25 — the *model* says an item is just an internal join key, not a Foodstuffs
thing; only the *implementation* still anchors on Foodstuffs.)

## The data (live DB, 2026-07-21, after D29)

Foodstuffs (NewWorld + PAK'nSAVE) is ~100% linked by construction. The orphans are all in the two Tier-2 chains:

| Chain | linked | pending review | zero-candidate | total |
|---|---|---|---|---|
| FreshChoice | 306 | 202 | **733** | 1241 |
| Woolworths (Takapuna) | 585 | 365 | **2452** | 3402 |

**Zero-candidate = the matcher produced *no* candidate at all** (failed the `brand+size` hard filter, or matched a
`(brand,size)` bucket but every name scored below the 0.3 propose threshold). Breakdown by *why*:

**FreshChoice, 733 zero-candidate:**
| # | % | bucket |
|---|---|---|
| 81 | 11% | loose / weight-sold — no fixed size to match on |
| 568 | 77% | no brand inferable from the name (see below — this bucket is itself mixed) |
| 62 | 8% | brand known, size format mismatch — **a true miss** |
| 22 | 3% | brand+size both exist, but name overlap < 0.3 → not proposed |

**Woolworths, 2452 zero-candidate:**
| # | % | bucket |
|---|---|---|
| 219 | 9% | loose / weight-sold |
| 466 | 19% | private label (Woolworths/Macro) — no Foodstuffs equivalent brand |
| 1290 | 53% | brand not stocked by Foodstuffs at all |
| 424 | 17% | brand known, size format mismatch — **a true miss** |
| 53 | 2% | brand+size exist, name overlap < 0.3 → not proposed |

FreshChoice's big "no brand inferable" bucket (568) is **not** what I first assumed (generic cut names). A sample
shows it's three things mixed: **Woolworths private label** ("WW Cheese Colby", "WW Fresh Whole Chicken" — 103 of
them, `WW`/`Woolworths`/`Macro` prefix), **real brands Foodstuffs doesn't carry** ("Laughing Cow", "Vitasoy",
"Good Buzz Kombucha", "Gourmet Garden"), and **genuine unbranded produce** ("Kiwifruit Gold Punnet", "Mushrooms
Shitake"). Each of those wants a different answer — see the categories.

## The four categories (by *right answer*, not by mechanism)

The orphans are not one problem. Sorted by what actually resolves them:

### A — No cross-store equivalent exists → don't match; make browsable (TD-5)
Private label unique to one chain + brands only one chain stocks. **~1756 of Woolworths' orphans (72%)** plus a
chunk of FreshChoice's. **AI cannot help** — there is nothing to match to; forcing a link would be a *correctness
error*. The right answer is TD-5: give each a **singleton item** so it's browsable/searchable as a standalone
product (today it's invisible to category-filtered browse, only reachable by text search).

> **The one nuance that turns some of A into matchable:** Woolworths private label is sold at **both** Woolworths
> **and** FreshChoice (same Woolworths-NZ supply). Those *can* match each other — just never to Foodstuffs. That
> requires items that can be **anchored on a non-Foodstuffs product** (the headline finding). So de-anchoring
> (below) converts "WW private label at 2 of our stores" from orphan → real cross-store compare.

### B — Matchable, but `brand+size` is the wrong key → fresh / produce / meat cuts
Loose-sold (300 across both) + generic-named fresh lines ("Beef Short Rib", "Whole Chicken", "Kiwifruit Gold").
These **are** genuinely comparable across stores, but they have no brand and often no fixed size, so `brand+size`
can *never* match them. They need matching on **category + semantic name** ("Beef Short Rib" ≈ "Beef Ribs Short
Cut") + `$/kg`. This is the **highest-value new capability** (meat/produce is the core department) and the clearest
place an **LLM / embeddings** earns its keep — semantic sameness is exactly what token-overlap can't do. It's also
the hardest, because the retrieval key changes from `(brand,size)` to category+embedding.

### C — True misses: same branded product, size *format* differs
**424 Woolworths + 62 FreshChoice (~17% / 8%).** Brand matches a Foodstuffs item; the size string just doesn't
normalise equal ("500g" vs "500 g" vs a pack-count vs a weight variance). Fix order: **(1) better size
normalisation** — cheap, deterministic, catches most (e.g. tolerate spacing, unit synonyms, pack-vs-weight); **(2)
an LLM judge** only for genuine variance. AI is overkill for most of C.

### D — Disambiguation: the existing review queue
**567 pending (202 FC + 365 WW).** Already shortlisted to 2–7 candidates; just needs a *picker*. **Lowest-risk,
cleanest AI use** — a bounded choice, not an open search. This is exactly what [ai-matching.md](ai-matching.md)
already designs (shortlist-then-pick, "none → new" escape, confidence thresholds).

## Where AI actually pays off (and where it doesn't)

AI is **not** a silver bullet for "the orphans." Grounded in the buckets above:

- **~72% of Woolworths orphans (Category A) have no match to find.** An LLM asked to match them can only
  hallucinate or (correctly) say "none" — a token bill for the answer "there isn't one." These need TD-5
  (browsability) and/or de-anchoring, **not** a matcher.
- **Best *first* AI use = Category D** (judge the 567-item review queue): bounded, low-risk, already designed.
  Turn ai-matching.md on here first; it replaces/augments human review without touching the search space.
- **Highest *new-value* AI use = Category B** (semantic fresh/produce matching): real capability token-overlap
  can't reach — but needs a different retrieval key (category + embeddings) and the de-anchoring prerequisite, so
  it's the biggest build.
- **Category C is mostly not an AI problem** — better size normalisation first, LLM only for the residue.

So the honest sequence isn't "add AI." It's: **fix the anchor → cheap deterministic wins → AI where semantics
genuinely dominate.**

## A phased path (proposal, for discussion)

1. **De-anchor items from Foodstuffs (the unlock).** Let an item be born from *any* store product (generalises
   TD-5's "singleton item"), then **merge** when a match is later found (existing merge machinery). Immediately:
   every orphan becomes browsable (TD-5 solved) **and** WW-private-label-at-both-WW-and-FC becomes matchable. This
   is the prerequisite for almost everything else and is pure plumbing (no AI).
2. **Tighten size normalisation (Category C).** Deterministic, cheap, measurable — recovers a good slice of the
   ~486 true misses with zero AI cost.
3. **Turn on the LLM review-queue judge (Category D, = ai-matching.md).** Bounded, low-risk, shrinks the 567
   pending set and earns trust/eval data for anything more ambitious.
4. **Semantic fresh/produce matching (Category B).** Category-scoped embedding retrieval + LLM judge, on the
   de-anchored item model. The big one; do it last, once 1–3 have de-risked it and produced an eval set.

## Decision — the anchor-priority cascade (2026-07-22 🧑‍⚖️ Kevin) — ✅ BUILT (D30)

> **Built & verified** in `ItemMatcher` (Tier 3/4), tests in `ItemMatcherTests`, live-run numbers below. See
> [matching.md](matching.md) for the running reference. Sizing predictions here held: 1,956 WW-anchored (61 real
> WW+FC 2-store groups), 529 FreshChoice singletons, 861 guarded-out. The category sub-question was resolved as
> **option A** (existing ~26% name mapping, non-blocking) for now.


**Every active product ends up in an item; the *anchor* is chosen by data quality, highest first.** Cross-store
compare is the point (Kevin: "跨店比价还是最重要的"), so we keep the item-centric model and get *every* unmatched
product into an item via a fallback cascade — a product only becomes a *new anchor* if it couldn't attach to an
item at a higher tier:

| Tier | Anchor | How a product joins | Yields |
|---|---|---|---|
| 1 | **Foodstuffs** (`foodstuffs:{sku}`) | shared `productId` | multi-branch groups (existing) |
| 2 | attach to a Foodstuffs item | WW/FC by `brand+size+name` | cross-store compare (existing) |
| 3 | **Woolworths** (`woolworths:{sku}`) | WW product, brand ∉ Foodstuffs-vocab → new item; FC attaches by `brand+size+name` | WW+FC compare + WW singletons |
| 4 | **FreshChoice** (`freshchoice:{sku}`) | FC product that attached to nothing above → new item | FC singletons (1 store → always singleton) |

Anchor priority = **Foodstuffs → Woolworths → FreshChoice**, following data quality: Foodstuffs is deterministic
(`productId`), Woolworths has a real `RawBrand`, FreshChoice has neither (brand inferred, D29). A lower-quality
source only *anchors* what the higher ones don't have; it *attaches* to a higher-tier item whenever it can.

**Why singletons are worth it (this reverses the earlier "does a singleton item add value?" doubt):** making
"**every active product has an `ItemId`**" an *invariant* removes null-`ItemId` special-casing from category browse,
search-dedup, and `/deals` — one uniform path instead of two. Tier-3/4 singletons are the price of that invariant;
the compare payoff is Tier 3's WW+FC groups (~118 today) plus everything that later merges in. **Category-browse
does *not* need items** (a product reaches the shared tree through its own `StoreCategory.CategoryId`, independent
of matching — see [matching.md](matching.md) / `CategoryMapper`), so browsability is **not** the reason for the
singletons; the *invariant* + future merges are. **Which `Category` a Tier-3/4 item shows as is still open** — see
the sub-section below (but it does not block: the underlying products are already category-mapped either way).

**The one correctness guard (quantified).** A product only becomes a **new anchor** if its brand is **not in the
Foodstuffs brand vocabulary** — *not* merely "Tier 2 didn't link it." Measured on the live DB: **861 Woolworths
products are unlinked but their brand *is* a Foodstuffs brand** — these are Tier-2 *misses* (size-format /
ambiguous-queue, Categories C & D). Anchoring them at Tier 3 would mint 861 duplicate Woolworths items for products
that belong to an existing Foodstuffs item, permanently *splitting* the cross-store compare. So these are the **one
deliberate exception** to the "every product gets an item" invariant: they stay unanchored, pushed toward the
*correct* Foodstuffs item via size-normalisation / the review queue. (Same logic guards Tier 4: an FC product whose
inferred brand is a Foodstuffs or Woolworths brand shouldn't anchor a new FC item — it should attach.)

**Sizing (live DB, 2026-07-22):**
| tier | | count | becomes |
|---|---|---|---|
| 3 | WW, brand ∉ Foodstuffs-vocab, has size | **1755** | Woolworths-anchored item, indexable by `(brand,size)` |
| 3 | WW, brand ∉ Foodstuffs-vocab, loose/no-size | **201** | Woolworths-anchored **singleton** |
| 3 | FC orphans that attach to a WW anchor by `(brand,size)` | **~118** | real 2-store (WW+FC) compare — impossible today |
| 4 | FC orphans that attach to nothing above | **~615** | FreshChoice-anchored **singletons** (1 store ⇒ always singleton) |
| — | *(guarded out)* WW unlinked but brand ∈ Foodstuffs-vocab | *861* | **not** an anchor — stays a Tier-2 miss |

So the cascade creates ~1,956 Woolworths-anchored + ~615 FreshChoice-anchored items. The *compare* payoff is the
~118 WW+FC groups (Woolworths-family private label + shared non-Foodstuffs brands — impossible to compare today);
the rest are singletons that satisfy the "every product has an `ItemId`" invariant and stand ready to **merge** the
moment a match appears. Tier-4 FC items are always singletons because there is one FreshChoice store and the two
higher-quality sources already had first claim.

**Reuses existing machinery** (low incremental build): D29 brand-inference for the FC side, the same
`(brand,size)` index + name-overlap policy, stable `MatchKey`s (`woolworths:{sku}` / `freshchoice:{sku}`, mirroring
`foodstuffs:{sku}`) for idempotent re-runs, and **merge** for the reverse edge — if a higher-tier source later
stocks the product, its item wins and the lower-tier item merges into it (Foodstuffs > Woolworths > FreshChoice).

### The open sub-question: what `Category` do Woolworths-/FreshChoice-anchored items get?

A Foodstuffs-anchored item inherits its category from its Foodstuffs member (CategoryMapper maps Foodstuffs → shared
`Category` **by identity**, 100%). A Woolworths-/FreshChoice-anchored item has **no Foodstuffs member**, so that path
doesn't apply. Its route to the shared tree is its own store-category → shared `Category` **by exact name**, which
matching.md measures at **~26%** for Woolworths (and FreshChoice is department-level only, D26) — so most would land
uncategorised under the status quo. **Note this does not affect browsability** (the products reach the tree through
`StoreCategory.CategoryId` regardless); it only affects the *item*'s displayed `Category` label. Options to discuss:

| Option | Coverage | Cost |
|---|---|---|
| **A. Use the existing WW-name→Category mapping** as-is | ~26% categorised, rest uncategorised | zero — already built |
| **B. Improve WW→Category matching** (fuzzy/curated aliases) | higher | moderate, ongoing curation |
| **C. Manual category assignment** for the Tier-3 set | 100% but hand-done | ~2k items to curate (one-off, then incremental) |
| **D. Let them sit uncategorised**, browsable only via search until curated | 0% browse, 100% search | zero |

My lean: **A now (free, ships the 26% immediately), C incrementally** for the high-traffic gaps — but this is the
call you flagged as open. It interacts with D25 (Category is the *one* curated, user-facing surface), so hand-curation
here is consistent with the model, just work.

## Open decisions (need Kevin's call before building)

- **De-anchor now, or keep Foodstuffs-anchored?** Everything else leans on step 1. It's a real Item-semantics
  change (matcher must create + later merge non-Foodstuffs items without thrashing on re-runs).
- **Browsable-but-unmatched vs. matched** — for Category A, is "shows up in its category as a standalone product"
  enough, or do we want cross-store grouping for the WW-family-private-label subset (needs de-anchoring)?
- **Build order of AI vs. deterministic** — the proposal front-loads the cheap deterministic wins (steps 1–2) and
  puts AI where it's uniquely needed (3–4). Agree, or pull AI forward?
- **Eval set** — before *any* auto-linking (heuristic or AI), a hand-labelled orphan sample to measure
  precision/recall. Who labels it, how big?
- **Cost ceiling** for the Category B embedding+LLM pass (one-time backfill + per-crawl incremental).

---

*Research doc — no code, no decision yet. The numbers are a 2026-07-21 snapshot and will drift each crawl; re-run
the bucket classification before trusting them for a build.*
