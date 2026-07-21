# zhua.food — tech-debt register

Deliberate shortcuts and layering inconsistencies we've **accepted for now**, each with where it lives, why it's
debt, the intended fix, and why it's deferred. **These are not bugs** — the code works and tests are green; they're
places the design isn't yet where we'd want it. The point of this file is that a shortcut is *tracked* instead of
silently forgotten.

**When you take a shortcut, add an entry here** (and a dated line to the Decision log). Reference it from the code
comment that takes the shortcut, so the two stay connected.

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`, to the minute), then 🧑‍⚖️ if user-instructed.

- **2026-07-11 21:37** — 🧑‍⚖️ *(Kevin)* Track tech debt in this register (first-class, **linked from CLAUDE.md**).
  Seeded from two issues spotted while reading the crawl/deals code: TD-1 (CrawlOrchestrator not moved to Application)
  and TD-2 (the "what is a deal" definition hard-coded in an Infra query). Keep it current as debt is added/paid down.
- **2026-07-20 15:50** — Added TD-3 (facet-split blind spot for products without a level1/level2 category) and TD-4
  (`PromoReport`/matcher ignore `IsAvailable`) — both accepted while building D28; not user-instructed shortcuts,
  logged so they aren't forgotten.
- **2026-07-21 10:30** — Added TD-5 (unmatched listings invisible to category-filtered browsing) — surfaced by the
  front-end's matching-coverage report; explicitly out of scope for D29 (FreshChoice brand inference only), kept
  separate because it's a bigger Item-semantics change (singleton items + auto-merge-on-match).

## Open items

### TD-1 — `CrawlOrchestrator` is a write-side use case stuck in Infrastructure

**Where:** [`src/Zhua.Infrastructure/Crawling/CrawlOrchestrator.cs`](../../src/Zhua.Infrastructure/Crawling/CrawlOrchestrator.cs)

**What:** The read side was moved to Application services over Domain repository ports in the repository refactor
(Infrastructure is meant to be pure EF adapters). `CrawlOrchestrator` — the *write* use case (open/close `CrawlRun`,
upsert `Product` by SKU, `LinkCategories` D11, `SyncTags` D13) — was **not** moved: it still lives in Infrastructure
and uses `ZhuaDbContext` directly. It's the one use case inconsistent with the rest.

**Why it's debt:** [`repository-refactor-plan.md`](repository-refactor-plan.md) justifies leaving it as "it drives
Playwright + the raw archive, a genuine infrastructure concern," but that's weak — it does **not** drive Playwright
(the crawler behind `IStoreCrawler.FetchAsync` does). What it actually does is use-case orchestration over persistence,
which is exactly an Application service's job. So the layering here is pragmatic, not principled.

**The fix:** Move it to `Zhua.Application` over new **write-side repository ports** (bulk-load products by SKU,
StoreCategory-tree upsert, chain-scoped tag-dimension upsert). The Domain rule it calls (`Product.ApplyObservation`,
D3/D23) already lives on the entity — only the *orchestration* moves.

**Why deferred / priority:** Lower than it looks. The write side is **Worker-internal — it is never reached by the
thin Api**, so the D27 "Api must not touch EF" hard rule doesn't even apply to it (the pressure that drove the read-side
refactor is absent). And the write path leans on EF change-tracking + navigation fixup, which is more work to recreate
over ports than the read ports were. **Revisit when** the write path needs to be swappable/unit-testable without EF, or
when it grows more logic.

### TD-2 — The "what is a deal" definition + saving live in an Infra query, not Domain

**Where:** [`src/Zhua.Infrastructure/Repositories/ProductRepository.cs`](../../src/Zhua.Infrastructure/Repositories/ProductRepository.cs)
(`FindSpecialsAsync`) + the saving arithmetic in [`DealQueries`](../../src/Zhua.Application/Deals/DealQueries.cs).

**What:** The business definition of "an active deal" — `IsOnSpecial && CurrentNonSpecialPrice != null &&
CurrentPrice != null`, ranked by the saving `CurrentNonSpecialPrice - CurrentPrice` — is **hard-coded as a LINQ
predicate/ordering in the Infrastructure repository**, with no Domain concept expressing it. (`Product.Saving` was
deliberately not added — a computed get-only property needs an EF `Ignore` and can't be used inside a
LINQ-to-SQL query anyway; see the deviation note in `repository-refactor-plan.md`.)

> Note: the *was-price reconstruction* rule (D23) that decides `IsOnSpecial`/`CurrentNonSpecialPrice` at crawl time
> **is** correctly in Domain (`Product.ApplyObservation`/`ReconstructWasPrice`). This item is only about the **read-side
> deal definition** consuming those persisted fields.

**Why it's debt:** A core business definition ("what counts as a deal", "what's the saving") lives in an infra query
instead of the Domain, so it can silently drift and can't be reused without copy-pasting the predicate.

**The fix (if reuse appears):** The **Specification pattern** — Domain exposes an EF-translatable
`Expression<Func<Product, bool>>` (e.g. `Product.ActiveDeal`) and a saving expression; the repository uses
`.Where(Product.ActiveDeal)`. This keeps **DB-side execution** (the predicate still translates to SQL, so filtering /
paging / ordering stay at the database) **and** puts the definition in Domain — no client-side evaluation.

**Why deferred / priority:** The deal predicate is used in **exactly one place** today (`FindSpecialsAsync`); the saving
in two. Inline is pragmatic for a single use. **Revisit when** "what is a deal" is needed in a 2nd/3rd place (deal
counts, deal-filtered browse, notifications) — that's when the spec pays for itself and drift becomes a real risk.

### TD-3 — Truncation facet-split can't see products with no `category1NI`/`category2NI`

**Where:** [`src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs`](../../src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs) (`CrawlScopeAsync`, D28)

**What:** When a department is Algolia-truncated (`totalHits` pinned at 1000) we re-crawl it as one query per
`category1NI` facet value (then `category2NI` if an aisle is still capped). A product inside that department whose
category tree has **no level1** (or no level2 under a capped aisle) matches none of the sub-queries and is
unreachable — invisibly, since the capped parent gives us no true total to check against.

**Why it's debt:** a small, silent coverage hole in exactly the departments big enough to be truncated. Likely tiny
(Foodstuffs trees are consistently 3 levels) but unproven.

**The fix:** after a split, one extra query per level filtered to "has no `category1NI`" is not expressible in the
Algolia filter syntax we mirror — realistic options are comparing the parent's facet-count sum against child sums,
or sampling the truncated parent's 1000 for SKUs the sub-scopes didn't return and flagging a gap if any exist.

**Why deferred / priority:** Low. The failure mode is "a few uncategorised products never enter the catalog", not
wrong prices; and reconciliation only retires products after **complete** runs, so at worst such a product flaps
in/out of availability if it also sits in an un-truncated department (then it's reachable anyway).

### TD-4 — `PromoReport` and admin/match surfaces ignore availability

**Where:** `PromoReport` (Worker), `/match-candidates` + matcher inputs.

**What:** D28 retires delisted listings from shopper queries, but the per-run promo-distribution report still counts
unavailable products, and the matcher/review queue still processes them.

**Why it's debt:** report percentages drift slightly from what shoppers can see; the matcher spends effort linking
listings nobody is served. Harmless today (retired rows are a tiny fraction).

**The fix:** filter `IsAvailable` in `PromoReport.BuildAsync`; decide whether the matcher should skip or keep
retiring listings (keeping them linked is arguably right — history pages still group by item).

**Why deferred / priority:** Low — cosmetic/efficiency, no user-visible wrongness; the matcher question deserves a
deliberate decision rather than a drive-by filter.

### TD-5 — Unmatched listings are invisible to category-filtered browsing

**Where:** [`ProductRepository.FindListingsAsync`](../../src/Zhua.Infrastructure/Repositories/ProductRepository.cs)
(and the identical predicate in `SpecialsQuery` for `/deals`) require `p.ItemId != null && p.Item!.CategoryId !=
null` when a `category=` filter is present.

**What:** A listing only gets a `Category` via its `Item.CategoryId`, and only matched listings have an `Item`.
So every unmatched product — all of FreshChoice pre-D29, most of Woolworths, and anything the matcher can't place
— is absent from `GET /categories/{id}/products` and `/deals?category=`, the primary browse path
([api.md](../api.md)'s documented "typical UI flow"). It's still reachable via `q=` text search on `/products`
(no category filter there requires `ItemId`).

**Why it's debt:** flagged by the front-end's matching-coverage report (2026-07-20) and confirmed against the
live DB. Not a correctness bug — nothing is mis-priced or mis-grouped — but a real gap in what's *browsable*: at
the time of writing, 948 FreshChoice + ~2,800 Woolworths listings (about a quarter of the catalog) can only be
found by search, never by clicking through categories.

**The fix:** give every unmatched listing a **singleton item** at crawl/match time (one item, one product, `Name`
seeded from the raw listing) so `CategoryMapper` can categorise it and it becomes browsable — then have the
matcher **merge** the singleton into the real cross-store item the moment one is found (reusing the existing
merge machinery, [matching.md](matching.md)#merge). Needs: (1) deciding whether `CategoryMapper` can categorise a
non-Foodstuffs singleton at all (today only Foodstuffs categorises by identity; Woolworths/FreshChoice map by
name, ~26% hit rate) — a singleton might still end up uncategorised, just differently uncategorised; (2) the merge
step must trigger automatically inside `ItemMatcher`, not wait for a human, or singletons pile up permanently.

**Why deferred / priority:** Medium — real user-facing gap (browse, not search, is the primary discovery path),
but it's a second architectural change (Item semantics, auto-merge-on-match) beyond what D29 scoped (FreshChoice
brand inference only). Do this next if the front-end confirms browse-completeness matters more than search
coverage for the affected quarter of the catalog.

## Paid-down items

_(none yet — move an item here with the commit/date that resolved it.)_
