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

## Paid-down items

_(none yet — move an item here with the commit/date that resolved it.)_
