# zhua.food — Architecture & Build Plan (`plan-cc`)

> Auckland grocery **price intelligence** platform. Information layer only.
> NOT e-commerce, NOT delivery, NOT a cart, NOT a marketplace.
> Goal of Milestone 1: **reliable scheduled crawling (default twice-daily) + historical price storage + search/compare APIs** on a clean, extensible architecture.

---

## 0. How to use this doc

- This is a living planning + decision doc. Edit freely.
- Section **2** is my review of your proposed design (you asked me to question it).
- Section **8** is the trackable checklist — update `[ ]` → `[x]` as we go.
- Section **9** is the decision log — record every "we chose X over Y because Z".
- 🟢 = I agree with your design · 🟡 = I'd change/refine it · 🔴 = needs your decision before we build.

---

## 1. Scope anchor (so we don't drift)

**Stores (Milestone 1):**

| Store | Chain | Owner | Latitude | Longitude | Notes |
|---|---|---|---|---|---|
| Woolworths Takapuna | Woolworths | Woolworths NZ | -36.7879 | 174.7695 | ex-Countdown |
| New World Takapuna | New World | Foodstuffs | -36.7868 | 174.7731 | |
| PAK'nSAVE Glenfield | PAK'nSAVE | Foodstuffs | -36.7783 | 174.7447 | |

*(lat/long = the geolocation we feed each site to select the physical store — D2/§10. `Store` seed data.)*

**Product coverage (Milestone 1):** common groceries only — milk, eggs, bread, bananas, apples, chicken breast, beef mince, pork belly, rice, cooking oil, vegetables, fruits, noodles, soy sauce, dairy. *Completeness is a non-goal; reliability + architecture is the goal.*

**Questions the system must answer:**
- Where is X cheapest right now?
- Which store has the lowest price for X?
- Is X on special this week?
- What is the price history of a product?
- How much could I save by shopping across stores?

---

## 2. Architecture review

### 2.1 🟢 Keep as-is (good calls)

- **Two logical pipelines (ingestion vs query) sharing one Postgres + shared entities.** Right level of separation for an MVP. No microservices, no queue, no event sourcing — agree.
- **Domain model** (Store / StoreProduct / CanonicalProduct / PriceSnapshot / CrawlRun). This is the correct decomposition. The `StoreProduct → CanonicalProduct` split is exactly the right shape for cross-store comparison.
- **Append-only price history** as a principle. Never mutate historical prices — agree.
- **Stack** (.NET 9 / EF Core / Postgres / Serilog / Docker Compose) — solid, boring, maintainable. 👍

### 2.2 🟡 Recommended changes (with rationale)

**R1 — Make ingestion and query *separate processes/containers*, not just separate layers.** ✅ **APPROVED**
Run `Zhua.Api` (web) and `Zhua.Worker` (crawler+scheduler) as **two deployables sharing the same class libraries and DB**.
*Why:* Playwright/Chromium is memory-heavy and occasionally crashes. If the crawler shares a process with the API, a browser leak or OOM takes down your query API too. Two processes give you fault isolation for almost zero extra complexity (you're already using Compose). This is the single most valuable change.

**R2 — Keep one `IStoreCrawler` abstraction so any store *can* use HttpClient or Playwright.** ✅ **APPROVED** — *decided (D2 rev): Playwright for all 3 in M1; abstraction retained so a store can switch to HttpClient later without touching the others.*
Define one `IStoreCrawler` abstraction; implementations may use **`HttpClient` (JSON/GraphQL API)** *or* **Playwright (browser)** internally.
*Why:* Woolworths NZ exposes a JSON product API that is far more reliable, faster, and cheaper than driving a browser. Foodstuffs sites (New World / PAK'nSAVE) are more JS-heavy / anti-bot and may genuinely need a browser (or careful header/cookie handling). Forcing Playwright everywhere makes the easy store as brittle as the hard one. **Action: spike each store's network tab before committing** (see Phase 1).

**R3 — `StoreProduct.CanonicalProductId` must be NULLABLE, and matching must be ASYNC/offline.** ✅ **APPROVED**
Crawl → store raw product + price *immediately*. Resolve to a CanonicalProduct as a *separate* step that can run later or be done by hand.
*Why:* Product matching ("Anchor Fresh Blue Milk 2L" @ Woolworths == "Anchor Blue Milk 2L" canonical == the New World SKU) is the hardest problem in the whole system. If ingestion blocks on matching, a matching bug stops all data collection. Decouple them: ingestion is dumb and always succeeds; matching is a curated/rules-based step on top. For MVP with ~15 categories × 3 stores, **manual / rules-based seed mapping is fine** — do not build ML matching yet.

**R4 — Denormalize "current price" onto `StoreProduct` for fast reads.** ✅ **APPROVED** (pairs with D3 change-only)
Each crawl: **always** update `StoreProduct.CurrentPrice / CurrentSpecial / LastSeenAt / PriceUpdatedAt` (fixed row count, no growth) — and append a `PriceSnapshot` **only when the price tuple changed** (see D3).
*Why:* "Where is milk cheapest *right now*" otherwise becomes a "latest snapshot per product" query that gets slower as history grows. The denormalized current-price columns keep the hot query trivial and always-fresh; the compact `PriceSnapshot` changelog holds history.

**R5 — Capture *unit price* and *special/was price*, not just the shelf price.**
- `UnitPrice` + `UnitOfMeasure` (e.g. $/L, $/kg) → required for honest comparison (2L vs 3L milk, 500g vs 1kg mince).
- `IsOnSpecial` + `NonSpecialPrice` (the "was" price) → required to answer "is X on special this week?".
- Consider `ClubPrice` (Woolworths Onecard / member pricing) and multibuy ("2 for $5") — at least store the shelf price cleanly and flag when a member/multibuy price exists.
*Why:* Comparison and "deals" are core features; raw price alone can't express them. NZ supermarket pages already expose unit price and was-price, so capture them at crawl time — you can't backfill what you didn't record.

**R6 — Crawl cadence must be configurable.** ✅ **APPROVED** (your requirement)
Cadence is config-driven (`appsettings`/env), with a **global default + optional per-store override**. **M1 default = twice daily** (e.g. ~06:00 / ~18:00), not hourly — see D7/Q4. Build the per-store override in now.
*Why:* you'll want to retune one store without touching code or affecting the others.

**R7 — Manual on-demand crawl (operator/dev), Worker-side only.** ✅ **APPROVED** (your requirement)
- The **public query API still never triggers crawling** — this is an operator action, not a user query, so it lives on the Worker, not the Api.
- **M1 / local:** Worker CLI one-shot — `dotnet run --project Zhua.Worker -- crawl [--store <chain>] --once` → runs once, exits, reuses the same `CrawlOrchestrator`.
- **Optional / later:** "run now" on a live instance by triggering the Quartz job (small internal admin surface on the Worker).

### 2.3 🔴 Open questions — need your decision

**Q1 — Snapshot write strategy. ✅ RESOLVED → (b) change-only (see D3).**
Append a `PriceSnapshot` **only when the price tuple changed**; otherwise just refresh liveness. ~1–2 orders of magnitude less storage than hourly-append (~50–100K vs ~13M rows/year at ~1,500 products), and every row is a real price-change event. Implementation rules:
1. **"Changed" = the full tuple** `{Price, IsOnSpecial, NonSpecialPrice, UnitPrice}` differs from the latest snapshot — not just the headline price (else "same $ but now on special" is lost).
2. **Always snapshot on first sighting** (no prior row to compare).
3. **`LastSeenAt` is load-bearing** — refreshed every crawl; it's how "price unchanged" is told apart from "product vanished / crawl failed" (with `CrawlRun` for run-level audit).
4. **Pairs with R4**: current-price columns updated every crawl for fast reads; `PriceSnapshot` only grows on change.
- History query (`/products/{id}/prices`) returns a clean **step function** — a price holds over the interval until the next change.

**Q2 — Scheduler. ✅ RESOLVED → Quartz.NET (see D4).**
The new requirements (R6 per-store configurable cadence + R7 on-demand "run now") are exactly Quartz's sweet spot, so the earlier "over-engineering?" worry is moot. Cron expressions come from config; the local one-shot (R7) calls the orchestrator directly and doesn't need the scheduler running.

**Q3 — Migration ownership. ✅ RESOLVED → dedicated one-shot `migrator` service in Compose (see D5).**
`Api` and `Worker` both wait for `migrator` to complete; neither auto-migrates on startup, so there's no migration race.

**Q4 — Data access strategy & ToS. ✅ RESOLVED (see D2 / D6 / D7).**
1. **API-first (gating spike, Phase 1).** Before writing any crawler, find what's available per store — and be precise about *which kind*:
   - **Official/sanctioned API** (documented, terms permit use): best on every axis. *Likely none for NZ grocery prices — must verify.*
   - **Internal site API** (the JSON/XHR the website itself calls): far more reliable than HTML scraping, so prefer it — **but still unsanctioned access, so it cuts ENGINEERING risk, not ToS risk.**
   - **HTML/browser (Playwright):** last resort.
2. **Concurrency is a non-issue (agreed).** One crawler process, stores crawled **sequentially**, pages within a store sequential → never a parallel hit on the same host. No concurrency-limiting machinery. (New World + PAK'nSAVE are both Foodstuffs and may share backend infra — if so, treat them as one host.)
3. **"Polite" reduces to:** modest **request spacing** (no bursts), an honest **User-Agent**, **honor `robots.txt`** where feasible, **back off on 429/5xx**, and **low frequency** — which the twice-daily cadence (D7) already provides. Low frequency is the single biggest ToS-risk reducer.
4. **Posture:** legitimate price-comparison use, accessed gently, ready to stop/adjust if a site objects.

---

## 3. Recommended solution structure (.NET 9)

Clean-Architecture layering, two entry points, crawlers isolated.

```
Zhua.Food.sln
├─ src/
│  ├─ Zhua.Domain/            # Entities, value objects, enums. NO external deps.
│  ├─ Zhua.Application/       # Use cases + interfaces (ISearch, ICompare, IStoreCrawler contracts, repo interfaces)
│  ├─ Zhua.Infrastructure/    # EF Core DbContext, configurations, migrations, repository impls
│  ├─ Zhua.Crawling/          # IStoreCrawler + per-store crawlers (HttpClient and/or Playwright), parsers, normalization
│  ├─ Zhua.Api/               # ASP.NET Core Web API (QUERY side). Thin controllers → Application services.
│  ├─ Zhua.Worker/            # Worker Service (INGESTION side). Quartz jobs → Crawling → Persistence.
│  └─ Zhua.Migrator/          # One-shot EF migration runner (plan D5; the Compose `migrator`).
└─ tests/
   ├─ Zhua.Domain.Tests/
   ├─ Zhua.Application.Tests/
   ├─ Zhua.Crawling.Tests/    # parser tests against saved HTML/JSON fixtures (golden files)
   └─ Zhua.Api.Tests/         # WebApplicationFactory / Testcontainers-Postgres integration tests
```

**Dependency direction:** `Domain` ← `Application` ← {`Infrastructure`, `Crawling`} ← {`Api`, `Worker`}. Domain depends on nothing. Api and Worker are the only executables.

**Per-store isolation lives in `Zhua.Crawling`:** one folder/class per store (`WoolworthsCrawler`, `NewWorldCrawler`, `PaknSaveCrawler`), each implementing `IStoreCrawler`. Adding a future store (Chinese/Korean/Indian supermarket) = add one class + register it. No edits to existing crawlers.

---

## 4. Domain model

```
Store ─1──*─ StoreProduct ─*──1(nullable)─ CanonicalProduct
                  │
                  1
                  │
                  *
            PriceSnapshot ─*──1─ CrawlRun ─*──1─ Store
```

| Entity | Key fields (M1) | Notes |
|---|---|---|
| **Store** | `Id`, `Chain`, `Name`, `Suburb`, `Latitude`, `Longitude`, `ExternalStoreId?`, `IsActive` | **Store context is geolocation** (lat/long) across all 3 chains (spike §10). `ExternalStoreId` = source-site store id where one exists (e.g. Woolworths, resolvable via its locator API). |
| **CanonicalProduct** | `Id`, `Name`, `Brand`, `Size`, `UnitOfMeasure`, `Category`, `Gtin?` | The normalized concept = an **exact item** (e.g. *Tegel Tenderbasted Chicken Breast 500g*). `Category` must be **fine-grained product-type** (*Chicken Breast*, NOT *Chicken* — see §10 anti-pattern). `Gtin`/barcode = **primary matching key** when present; brand+size is the fallback. Store private labels (Woolworths/Pams/Pak'nSave) are **distinct** canonicals. |
| **StoreProduct** | `Id`, `StoreId`, **`CanonicalProductId?`**, `SourceSku`, `RawName`, `RawBrand`, `RawSize`, `Url`, `ImageUrl`, `CurrentPrice`, `CurrentSpecial?`, `IsOnSpecial`, `UnitPrice?`, `UnitOfMeasure?`, `FirstSeenAt`, `LastSeenAt`, `PriceUpdatedAt` | Raw as-seen-in-store record. Nullable canonical FK (R3). Denormalized current price (R4); `LastSeenAt` refreshed every crawl as liveness (D3). |
| **PriceSnapshot** | `Id`, `StoreProductId`, `CrawlRunId`, `Price`, `NonSpecialPrice?`, `IsOnSpecial`, `UnitPrice?`, `Currency`, `CapturedAt` | Append-only changelog — **one row per price-tuple change** (D3), not per crawl. Linked to the run that produced it. |
| **CrawlRun** | `Id`, `StoreId`, `StartedAt`, `FinishedAt?`, `Status` (Running/Succeeded/Failed/Partial), `ProductsFound`, `SnapshotsWritten`, `ErrorMessage?` | Observability/audit trail per store per run. |

**The hard part (flagged):** `StoreProduct → CanonicalProduct` resolution. M1 approach = curated seed table + simple rules (brand + normalized size + category, GTIN when present). No ML.

---

## 5. Ingestion pipeline design

```
Quartz trigger (default twice-daily)
  → CrawlOrchestrator (per active store, low concurrency)
      → IStoreCrawler.FetchAsync()         # Playwright (browser) for all 3 stores (D2); intercept the page's JSON response & parse THAT — DOM scrape only as fallback
      → Parse (site-specific, fixture-tested)
      → Normalize (price, unit price, special, units)
      → Persist:  open CrawlRun → upsert StoreProduct (refresh current price + LastSeenAt, R4)
                  → append PriceSnapshot ONLY IF price tuple changed (D3) → close CrawlRun
```

- **Transport (D2):** Playwright (Chromium) for all 3 stores — one uniform pattern, and the real browser carries store cookies + survives anti-bot. **Parse the JSON the page fetches (`page.on("response")` interception), not the DOM** — the JSON contract is far more stable than HTML/CSS, so this is the *less*-fragile choice; DOM scraping is fallback only.
- **Politeness/robustness:** stores crawled **sequentially** (single process, no same-host concurrency); modest request spacing + retry/backoff on 429/5xx + timeout; **every run wrapped in a `CrawlRun`** so failures are observable, not silent.
- **Matching is a separate concern** (R3): a `CanonicalMatcher` step (or manual admin action) assigns `CanonicalProductId` after the fact. Ingestion never blocks on it.
- **Parser fixtures:** save real sample responses as test fixtures; parser unit tests run against them so a site layout change fails a test instead of silently producing garbage.
- **Schedule (R6/D7):** Quartz cron per store, cadence from config — **default twice-daily**, optional per-store override; `Store.IsActive` gates whether it runs.
- **Manual trigger (R7):** the same `CrawlOrchestrator` is invokable as a Worker CLI one-shot (`crawl [--store <chain>] --once`) for local dev/debug; the public Api never triggers it.

---

## 6. Query pipeline design

```
Frontend → Zhua.Api (thin controllers) → Application services → EF Core read queries → Postgres
```

The API **never** triggers crawling in M1. Read-only over already-persisted data.

| Endpoint | Backed by |
|---|---|
| `GET /search?q=milk` | `CanonicalProduct` name search → its `StoreProduct`s → `CurrentPrice` (fast, via R4) |
| `GET /compare?q=milk` | **(exact)** same `CanonicalProduct` across stores → which store is cheapest for *that item* (the differentiator, D9); **(type)** cheapest within a fine-grained category by `UnitPrice`, incl. private labels. NEVER whole-category $/kg — meaningless (§10). |
| `GET /deals` | `StoreProduct` where `IsOnSpecial = true`, ranked by discount vs `NonSpecialPrice` |
| `GET /products/{id}/prices` | `PriceSnapshot` changelog → **step-function** time series (price holds until the next change, D3) |
| `GET /health`, `GET /admin/crawl-runs` | liveness + last-run observability (CrawlRun) |

---

## 7. Cross-cutting

- **Migrations:** single owner (see Q3). Dedicated `migrator` one-shot service in Compose is my lean.
- **Config:** connection string + crawl cadence (global default + per-store override, R6) + per-store toggles via env vars / `appsettings`. Stores marked `IsActive` so you can pause one without code changes.
- **Logging/observability:** Serilog structured logs (console + optionally file); `CrawlRun` as the domain-level audit trail; `/admin/crawl-runs` to eyeball health.
- **Testing:** Domain unit tests · parser fixture (golden-file) tests · Api integration tests with WebApplicationFactory + Testcontainers-Postgres.
- **Docker Compose:** `postgres`, `migrator` (one-shot), `api`, `worker`.

---

## 8. Milestone 1 plan — trackable checklist

### Phase 0 — Foundations
- [x] Decisions: R1–R3 approved · Q1→change-only · Q2→Quartz · Q3→migrator · Q4→API-first + twice-daily · R6/R7 added — **all open questions resolved**
- [x] Create solution + 7 projects per §3 (incl. `Zhua.Migrator`), clean-arch references
- [x] Add `Zhua.Domain` entities (§4) — Store(+lat/long), CanonicalProduct(+GTIN), StoreProduct, PriceSnapshot, CrawlRun
- [x] EF Core `DbContext` + configs + `InitialCreate` migration (5 tables, 3-store seed) — builds + renders DDL; live-apply pending Docker
- [x] Docker Compose: `postgres` + one-shot `migrator` (+ Dockerfile, `.gitignore`/`.dockerignore`/`.gitattributes`, README)

### Phase 1 — Ingestion spike (de-risk first!)
- [x] **Step 1 — prior-art recon (done):** existing NZ scrapers found; per-store strategy drafted (§10). Woolworths → JSON API (`api.cdx.nz`); New World + PAK'nSAVE → Playwright + geolocation.
- [x] **Step 2 — formal spike skipped (D2 rev):** standardized on Playwright-for-all, no up-front endpoint verification. Folded into build: grab Takapuna/Glenfield lat/long, confirm GTIN presence in the intercepted JSON during crawler dev, check each `robots.txt`.
- [ ] Define `IStoreCrawler` + `CrawlRun` lifecycle
- [ ] Implement first crawler end-to-end (suggest **Woolworths** — likely the easiest) → real rows in Postgres
- [ ] Parser fixtures + tests for that store

### Phase 2 — Full ingestion
- [ ] Implement New World + PAK'nSAVE crawlers (browser or API per spike)
- [ ] `CrawlOrchestrator` + Quartz schedule, cadence from config (default twice-daily + per-store override, R6/D7); stores crawled sequentially
- [ ] Persistence: refresh StoreProduct current price + LastSeenAt (R4); append PriceSnapshot only on price-tuple change (D3)
- [ ] Worker CLI one-shot manual crawl `crawl [--store <chain>] --once` (R7)
- [ ] Politeness: rate limit, retry/backoff, per-run error capture

### Phase 3 — Canonical matching (CORE M1 feature — D9; runs offline, decoupled from ingestion per R3)
- [ ] Seed `CanonicalProduct`s for the ~15 **fine-grained** M1 product-types
- [ ] `CanonicalMatcher`: **GTIN-first** (same barcode = same item), then brand + normalized size; private labels stay distinct
- [ ] Capture `Gtin` during crawl wherever the source exposes it (primary matching key)
- [ ] Admin/manual review + override for `StoreProduct.CanonicalProductId` (ambiguous / no-GTIN cases)

### Phase 4 — Query API
- [ ] `GET /search`
- [ ] `GET /compare` (by unit price)
- [ ] `GET /deals`
- [ ] `GET /products/{id}/prices`
- [ ] `GET /health` + `GET /admin/crawl-runs`
- [ ] Api integration tests

### Phase 5 — Hardening
- [ ] End-to-end: scheduled (twice-daily) crawl runs unattended for several days, snapshots accumulate, APIs answer the 5 core questions
- [ ] Serilog dashboards/log review; alert on failed CrawlRuns
- [ ] README + run instructions

---

## 9. Decision log

| # | Decision | Choice | Rationale | Date |
|---|---|---|---|---|
| D1 | Api vs Worker process split | ✅ Two processes (R1) | fault isolation from Playwright | 2026-06-20 |
| D2 | Crawler strategy per store | ✅ **Playwright (browser) for all 3**, intercepting the page's **JSON** (not DOM); DOM parse = fallback. Geolocation store context (R2; §10) | one uniform pattern to maintain + browser anti-bot cover; JSON contract is more stable than HTML. (Note: user's "API is fragile" premise is inverted — HTML is the fragile layer — but Playwright-for-all stands on uniformity + anti-bot grounds) | 2026-06-20 (rev) |
| D3 | Snapshot write strategy | ✅ Change-only (full price tuple) + always refresh current/LastSeenAt | ~100× less storage; each row is a real change event | 2026-06-20 |
| D4 | Scheduler | ✅ Quartz.NET | needed for per-store cadence (R6) + on-demand run (R7) | 2026-06-20 |
| D5 | Migration owner | ✅ One-shot `migrator` service in Compose; Api/Worker wait, no auto-migrate | avoids two-process migration race | 2026-06-20 |
| D6 | Data access / ToS stance | ✅ API-first (official > internal JSON > browser); sequential, gentle, robots-aware, ready to stop | low frequency is the main risk reducer; internal-JSON cuts engineering risk only, not ToS | 2026-06-20 |
| D7 | Crawl cadence | ✅ **Default twice-daily**, config-driven, global + per-store override (R6) | groceries don't change hourly; lighter + lower ToS risk; with D3, cadence = detection latency and sub-day is fine | 2026-06-20 |
| D8 | Manual crawl trigger | ✅ Worker-side: CLI one-shot now; Quartz "run now" later (R7) | public Api never triggers crawls | 2026-06-20 |
| D9 | Canonical matching scope | ✅ **Core M1 feature, NOT deferred.** Two compare levels: exact same-product + fine-grained type | category-level $/kg is meaningless (whole chicken vs breast vs drumsticks); same-product cross-store compare is the differentiator | 2026-06-20 |

---

## 10. API spike — Step 1 recon findings (2026-06-20)

Prior-art recon done (web research). **Decision (D2 rev): all 3 stores use Playwright, reading the page's JSON via network interception; the formal DevTools spike was skipped** (standardize over verify). Per-store notes:

| Store | Strategy | Store context | Confidence | Notes |
|---|---|---|---|---|
| Woolworths | **Playwright → intercept JSON** | lat/long | — | Standardized on Playwright-for-all (D2 rev). A JSON API exists (`api.cdx.nz`) — we read it via Playwright network interception (not HttpClient) for a uniform pattern + anti-bot cover. |
| PAK'nSAVE | **Playwright (browser)** | geolocation lat/long in config | High | Confirmed by `Jason-nzd/pakn-scraper` — **.NET 8 + Playwright** (our stack); site geolocates to nearest store. |
| New World | **Playwright (browser)** | geolocation lat/long (assumed) | Med | Same parent (Foodstuffs) + same site tech as PAK'nSAVE → assume same approach; confirm in DevTools. |

**Cross-cutting finding — store context is geolocation everywhere.** All three pick the physical store from **lat/long**, not a store-id path → `Store` carries `Latitude`/`Longitude` (Takapuna, Glenfield); that's how every crawler targets the right store. (Woolworths also has a numeric store id resolvable via its site-locator API.)

**No sanctioned public price API** (Woolworths' official portal is AU loyalty/partner; NZ loyalty runs on Apigee). So Woolworths = internal JSON (engineering-easy, still unsanctioned), Foodstuffs = browser. D6 posture unchanged.

**Reference implementations to study (not dependencies):**
- `Jason-nzd/pakn-scraper` — PAK'nSAVE, **.NET 8 + Playwright + CosmosDB price history** — closest to our stack.
- `Jason-nzd/supermarket-prices-nextjs` — full NZ price tracker with history (Next.js) — overall-shape reference.
- `TonyCui02/grocer` (MIT) — all NZ supermarkets (React/Node/Python).
- `jesmcc/GroceryCompare` (Flask) — all three stores.

**Key UX lesson — the category-$/kg anti-pattern.** kiwiprice sorts a whole category (e.g. *Chicken*) by `$/kg`, which is meaningless: it conflates whole chicken (~$6/kg), drumsticks (~$5/kg) and breast (~$15/kg), and shows *the same product at two stores as two separate cards* (e.g. Ingham's Frozen Whole Chicken appears under both PAK'nSAVE and New World). → Our answer (D9): **fine-grained product-types + `CanonicalProduct` same-product grouping**. This is the core differentiator, not an optional extra.

---

*Created by Claude Code as the initial planning artifact. Only `plan-cc.md` has been modified; Step 1 recon was web research only.*
