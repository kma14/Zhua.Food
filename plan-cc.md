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

**Stores (Milestone 1): 3 branches per chain (9 total, D16)** — to compare same-brand branch prices.

| Store | Chain | Suburb | storeId source | Notes |
|---|---|---|---|---|
| Woolworths Takapuna | Woolworths | Takapuna | geolocation | ex-Countdown; **only active Woolworths** |
| Woolworths Glenfield | Woolworths | Glenfield | geolocation | **inactive** — national pricing (D16), kept as evidence |
| Woolworths Browns Bay | Woolworths | Browns Bay | geolocation | **inactive** — national pricing (D16) |
| New World Metro Auckland | New World | CBD | `ExternalStoreId` | CBD store first crawled (was mis-seeded as "Takapuna") |
| New World Shore City | New World | Takapuna | `ExternalStoreId` | the actual Takapuna branch |
| New World Browns Bay | New World | Browns Bay | `ExternalStoreId` | |
| PAK'nSAVE Albany | PAK'nSAVE | Albany | `ExternalStoreId` | only North Shore online store (Wairau is in-store-only) |
| PAK'nSAVE Botany | PAK'nSAVE | Botany (East) | `ExternalStoreId` | Chinese-dense |
| PAK'nSAVE Highland Park | PAK'nSAVE | Highland Park (East) | `ExternalStoreId` | Chinese-dense |

*(lat/long = the geolocation we feed each site to select the physical store — D2/§10. Foodstuffs storeIds pinned via `ExternalStoreId`. `Store` seed data.)*

**Finding (D16):** same-brand branch price variation, measured live — **Woolworths 0%** (738 shared products, identical across all 3 branches = national pricing), **New World 39.6%**, **PAK'nSAVE 48.9%** differ (avg gap ~$1.4–1.6, max $20–30). Foodstuffs stores are independently owned/priced; this validates per-physical-store price storage + branch-level "where's it cheapest".

**Product coverage (Milestone 1):** crawl the **full catalog via each store's own category tree** (Browse → Department → Aisle → Shelf), tagging every product with its store category (**D10**). This supersedes the earlier "~15 common types" guess — following the site's taxonomy is more correct, complete, and yields clean fine-grained categories (feeds `RawCategory` + D9). M1 departments (expanding): **Woolworths** = Meat & Poultry + Fruit & Veg + Fish & Seafood + Fridge & Deli + Frozen; **Foodstuffs** (NW/PAK'nSAVE) = Meat Poultry & Seafood + Fruit & Vegetables + Fridge Deli & Eggs + Frozen (Foodstuffs folds seafood into the meat department). *(All crawlers live; counts match each source — e.g. Woolworths Fridge & Deli 1877 ≈ site's 1884.)*

**Woolworths request cost (D17):** Woolworths products don't self-describe their category, so building the product↔shelf tree means querying every aisle+shelf → **~300 requests/crawl** vs Foodstuffs' **~40/store** (department-level query; products carry `categoryTrees`). The volume trips Woolworths' WAF rate-limit, so the crawler does **cooldown-and-retry with session refresh** on a block (12/24/36s). Option (not taken): crawl Woolworths at aisle level to ~halve requests, trading shelf-level granularity.

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
  │               │  │ │
  │               │  │ └─*──*─ ProductTag      (m2m, promo badges — D13)
  │               │  └───*──*─ StoreCategory   (m2m, Dept→Aisle→Shelf tree — D11)
  1               1
  │               │
  *               *
StoreCategory   PriceSnapshot ─*──1─ CrawlRun ─*──1─ Store
```

| Entity | Key fields (M1) | Notes |
|---|---|---|
| **Store** | `Id`, `Chain`, `Name`, `Suburb`, `Latitude`, `Longitude`, `ExternalStoreId?`, `IsActive` | **Store context is geolocation** (lat/long) across all 3 chains (spike §10). `ExternalStoreId` = source-site store id where one exists (e.g. Woolworths, resolvable via its locator API). |
| **CanonicalProduct** | `Id`, `Name`, `Brand`, `Size`, `UnitOfMeasure`, `Category`, `Gtin?` | The normalized concept = an **exact item** (e.g. *Tegel Tenderbasted Chicken Breast 500g*). `Category` must be **fine-grained product-type** (*Chicken Breast*, NOT *Chicken* — see §10 anti-pattern). `Gtin`/barcode = **primary matching key** when present; brand+size is the fallback. Store private labels (Woolworths/Pams/Pak'nSave) are **distinct** canonicals. |
| **StoreProduct** | `Id`, `StoreId`, **`CanonicalProductId?`**, `SourceSku`, `RawName`, `RawBrand`, `RawSize`, `Gtin?`, `Url`, `ImageUrl`, `CurrentPrice`, `CurrentNonSpecialPrice?`, `IsOnSpecial`, `UnitPrice?`, `UnitOfMeasure?`, `FirstSeenAt`, `LastSeenAt`, `PriceUpdatedAt`, `Categories[]`, `Tags[]` | Raw as-seen-in-store record. Nullable canonical FK (R3). Denormalized current price (R4); `LastSeenAt` refreshed every crawl as liveness (D3). m2m to `StoreCategory` (D11) + `ProductTag` (D13). |
| **StoreCategory** | `Id`, `StoreId`, `Kind` (Department/Aisle/Shelf), `ExternalId`, `Slug`, `Name`, `ParentId?` | The store's own category tree (D11), self-referencing. Unique `(StoreId, Kind, ExternalId)`. m2m with `StoreProduct`. |
| **ProductTag** | `Id`, `Chain`, `Source` (Primary/Additional), `Code`, `Label?` | Promo/marketing badge dimension (D13), chain-scoped, unique `(Chain, Source, Code)`. m2m with `StoreProduct`, **reset each crawl** (volatile; not snapshotted). |
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

Legend: ✅ done · 🚧 in progress · 🔲 todo

### Phase 0 — Foundations ✅
- ✅ Decisions R1–R3 + Q1–Q4 + R6/R7 resolved
- ✅ Solution + 7 projects (clean-arch references)
- ✅ `Zhua.Domain` entities (§4)
- ✅ EF `DbContext` + configs + `InitialCreate` (verified live: local + Compose migrator)
- ✅ Docker Compose: `postgres` (host **5433**) + one-shot `migrator`
  - ⚠️ pin `Microsoft.EntityFrameworkCore.Relational` in the executables, else the migrator runs on EF 9.0.1 and **silently no-ops**.

### Phase 1 — Ingestion spike ✅
- ✅ Prior-art recon (§10); per-store strategy
- ✅ Spike folded into build (Playwright-for-all, D2 rev)
- ✅ Ingestion contracts + `CrawlOrchestrator` (D3 change-only + R4 refresh)
- ✅ Woolworths crawler end-to-end → real Postgres rows (browse-by-category, D10)
- ✅ Worker CLI one-shot runner + parser golden-file tests

### Phase 2 — Full ingestion ✅
- ✅ New World + PAK'nSAVE crawlers — shared `FoodstuffsCrawler` (D15)
- ✅ Persistence: change-only snapshots (D3) + LastSeenAt refresh (R4)
- ✅ Worker CLI manual crawl `crawl [--store <chain>]`
- ✅ Politeness: base delay + Woolworths WAF cooldown-retry/backoff (D17)
- ✅ **Beyond plan:** `StoreCategory` tree (D11) · raw archive (D12) · promo tags (D13) · **9 stores, 3 branches/chain** (D16, Woolworths reduced to 1 active) · departments expanding to Fridge/Deli + Frozen (D17)
- ✅ Quartz scheduler (D4/D7): Worker with **no args** = scheduled mode — cron-driven `IngestionJob` (crawl all active stores → match), `Crawl:Cron` config (default twice-daily `0 0 6,18 * * ?`, local tz), `[DisallowConcurrentExecution]`. CLI `crawl`/`match`/`recon` still work as one-shots.

### Phase 3 — Canonical matching ✅ core done (D18; offline `match` command, decoupled per R3)
- ✅ **Tier 1 (free):** group Foodstuffs NW↔PAK by shared `productId` → one `CanonicalProduct` per SKU (upserted by `MatchKey`) — same-product compare across all 6 Foodstuffs stores. **3783 canonicals.**
- ✅ **Tier 2 (review-gated):** Woolworths↔Foodstuffs by brand + normalised size + name-token overlap; single clear winner ≥0.8 auto-links, ambiguous/weak → `MatchCandidate` review queue. **545 Woolworths auto-linked, 760 pending review.**
- ✅ **Review queue persists:** `MatchCandidate` (Pending/Approved/Rejected); matcher is **idempotent + re-runnable after each crawl**, honours human decisions, never re-asks. (Approve/reject endpoint = Phase 4.)
- ✅ **Canonical category (D22):** shared `CanonicalCategory` tree seeded from the Foodstuffs taxonomy; `CanonicalCategoryMapper` runs after the matcher and categorises **3857/3857** canonical products + maps store categories (NW/PAK 100%, WW 26% by name). Gives the UI a cross-store browse/select taxonomy.
- 🔲 **Fresh/unbranded:** still compare by category + `$/kg` (no brand/size → not canonicalised)
- 🔲 **Better cross-store category mapping:** Woolworths/FreshChoice shelves that don't name-match (WW 74%) → fuzzy + review queue (reuse the D18 pattern)
- ⚠️ **D9 revised:** Foodstuffs exposes **no GTIN**, so GTIN-first can't bridge chains; the bridge is brand+size+name (D18)

### Phase 4 — Query API 🚧 (D20)
- ✅ `GET /products/search?q=` (canonical search, cheapest price + store count)
- ✅ `GET /products/{id}` — **same-product cross-store compare** (cheapest first, each store's own name + price)
- ✅ `GET /products/{id}/price-history` — per-store step series from the D3 snapshots (`?days=N`)
- ✅ `GET /stores` (active stores: supermarket, suburb, geo, product count, last-crawl freshness; `?supermarket=` filter)
- ✅ `GET /categories` (canonical tree, D22, `?kind=department|aisle` depth cap) + `GET /categories/{id}/products` ≡ `GET /products?category={id}` (merged-across-stores, cheapest store, normalised unit price)
- ✅ **`?storeId=` store filter** (repeatable list) on `/categories`, `/categories/{id}/products`, `/products?category=`, `/products/search` — scopes results to the shopper's chosen stores and recomputes price/count within them (Foodstuffs is per-branch priced, D16); ids from `/stores`
- ✅ Product images: `imageUrl` on search/category/compare/deals — Woolworths CDN + Foodstuffs fsimg derived (D24)
- ✅ `GET /deals?supermarket=` (current specials, biggest saving first; was-price reconstructed for NW/PAK — D23)
- ✅ `GET /admin/match-candidates` (+`storeProductId`/`candidateCanonicalId`) + `POST .../{id}/approve` · `.../{id}/reject`; plus `POST /admin/store-products/{id}/link-canonical` (link to an existing canonical) and `.../create-canonical` (mint a new one) — the 3 reviewer outcomes when candidates don't fit (Codex UI feedback)
- ✅ `GET /health` + `GET /health/db`
- ✅ Renamed the API-facing `chain` field/param → **`supermarket`** (Domain enum `Chain` stays internal); added price dates (`priceAsOf` = LastSeenAt, `priceUpdatedAt` = D3 change time) on every priced response. Front-end reference kept current in [docs/api.md](docs/api.md).
- ✅ **Api integration tests** (`tests/Zhua.Api.Tests`, 27) — `WebApplicationFactory<Program>` + **Testcontainers** Postgres, migrated + seeded with a known fixture; covers every endpoint (happy path, `?storeId=`/`?supermarket=` filters, price math, images, 400/404, admin approve/reject). DbContext swapped to the container via `ConfigureTestServices` (not config) so the dev DB is never touched.
- 🔲 `GET /admin/crawl-runs` (observability)
- 🔲 Auth on `/admin/*` (currently open — local only)

### Phase 5 — Hardening 🔲
- ✅ Containerize the Worker (D21) — Playwright image + self-contained net9 + direct `Xvfb` entrypoint; verified headed crawl in-container
- 🔲 Deploy to Synology DS220+ (registry push/pull + bind-mounts + SSH compose)
- 🔲 End-to-end: scheduled crawls run unattended for days, snapshots accumulate, APIs answer the 5 core questions
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
| D10 | Crawl strategy | ✅ **Browse the store category tree** (Department→Aisle→Shelf), paginate each category, tag each product with its store category — NOT hard-coded keyword searches. M1 depts: **Meat & Poultry, Fruit & Veg, Fish & Seafood** | follows the site's own taxonomy → complete coverage + precise fine-grained categories; supersedes the 15-term search | 2026-06-21 |
| D11 | Category as first-class | ✅ **`StoreCategory` tree entity** (Kind=Department/Aisle/Shelf, self-ref Parent) + **many-to-many** with `StoreProduct` — NOT a denormalized text field. Aisles/shelves **auto-discovered** from each browse response's `dasFacets` (no hard-coded slugs below department). | a product sits under several shelves; the tree mirrors the site and powers search/browse. Verified counts match site exactly: Meat&Poultry 267 / Beef 76 / Steak 20 | 2026-06-21 |
| D12 | Raw-response archive | ✅ **Archive every raw crawl response to disk**, default-on, self-pruning **7-day** retention (`ZHUA_CRAWL_DUMP_DIR` / `_RETENTION_DAYS`, disable `ZHUA_CRAWL_DUMP=0`). `crawl-archive/{chain}/{runTs}/{path}_pN.json`, git-ignored. | retrospective debugging — parsed DB keeps only mapped fields, so without raw bodies we can't see why a parse went wrong or recover newly-needed fields | 2026-06-21 |
| D13 | Promo tags | ✅ **`ProductTag` dimension (Chain, Source, Code) + many-to-many** with `StoreProduct`; **reset every crawl** (volatile, NOT in price history). Captures Woolworths `productTag.tagType` (IsSpecial / **IsGreatPrice = "Low Price"** / IsClubPrice / IsFreshDeal / IsGreatPriceMultiBuy / IsNew; "Other" dropped). `Source` column future-proofs `additionalTag` (Clearance/Organic/own-brand). Keep existing `IsOnSpecial`+was-price as the real discount signal (D3). | the source's promo badges are orthogonal to the `isSpecial` bool (a product can be isSpecial **and** show an IsClubPrice badge), so a single bool can't reproduce the site; m2m absorbs new tag values without migrations | 2026-06-22 |
| D14 | Promotion history | ✅ **Don't historize promotions.** Tags stay current-state only (reset per crawl = current badge for UX); **`PriceSnapshot` is the sole history of record** (each price-tuple change + date, D3). No `StartedAt`/`EndedAt` on tags, no `Promotion` entity. | source doesn't return promo start/end dates anyway (0/739 — gated by `EnableReturnOfPromotionStartAndEndDate`); price-special periods are already reconstructable from snapshots; promo-badge history has little user value | 2026-06-22 |
| D15 | Foodstuffs crawler | ✅ **One shared `FoodstuffsCrawler` base for New World + PAK'nSAVE** (same platform/API; only domain + store differ). POST `api-prod.{site}/v1/edge/search/paginated/products` (Algolia-backed), filtered by department name (`category0NI`), paginated by `totalPages`. Needs an **anonymous Bearer token** — captured from the page's own api-prod requests during warmup. Each product carries `categoryTrees[{level0/1/2}]` (→ Department/Aisle/Shelf, often several), so we crawl per-department and the product self-describes its path(s). Price is in **cents**. **No GTIN, no image URL** in this API (canonical falls back to brand+name; image via fsimg CDN later). storeId via `ExternalStoreId` or geolocation. Verified: NW Beef=24, NW=328 / PAK'nSAVE=641. | shared platform → one crawler covers two banners ~free; shared Foodstuffs `productId` makes **cross-banner same-product compare (D9) nearly free** — and NW-vs-PAK'nSAVE price gaps are the core "where's it cheapest" value | 2026-06-22 |
| D16 | 3 branches per chain | ✅ **Seed 3 stores per chain (9 total)** to compare same-brand branch prices. Woolworths = Takapuna/Glenfield/Browns Bay (geolocation); New World = Metro/Shore City/Browns Bay; PAK'nSAVE = Albany/Botany/Highland Park (only Albany is online on the Shore; Botany+Highland Park are the Chinese-dense online stores). **Result → only 1 Woolworths kept active** (the other two deactivated; national pricing makes them redundant). | **measured: Woolworths 0% / NW 39.6% / PAK'nSAVE 48.9%** of shared products differ in price across branches — Foodstuffs is franchise-priced, so branch matters; proves per-store pricing + branch-level compare | 2026-06-22 |
| D20 | Query API | ✅ **Controller-based read endpoints** in `Zhua.Api` (`[ApiController]` controllers + DTOs in `Contracts`; converted from minimal-API on 2026-06-27 per preference — same routes/shapes): `/products/search`, `/products/{id}` (same-product compare — the core view), `/deals`. Plus an **admin match-review** group (`/admin/match-candidates` + approve/reject) — the only writes, touching already-ingested data (no crawl/migrate, CLAUDE.md); approve links the product + clears its sibling candidates. Api uses `AddPersistence` only (D19). | turns the pipeline's data into the answers to the 5 core questions + lets the review queue be cleared over HTTP instead of SQL; verified live (e.g. Rolling Meadow Colby 1kg = PAK $12.56 special vs NW $16.56) | 2026-06-23 |
| D19 | Architecture cleanup | ✅ **Targeted de-anemia + hygiene** after a code review. Moved the **D3 change-only price rule onto `StoreProduct.ApplyObservation`** (Domain value object `StoreProductObservation` keeps Domain independent of Application's `ScrapedProduct`); the orchestrator now just maps + links the snapshot to its run and extracts `LinkCategories`/`SyncTags`. **Category/tag linking + crawl-run lifecycle stay in the orchestrator** (use-case orchestration over DB-loaded dimensions — not entity behaviour). Split `AddInfrastructure` → `AddPersistence` (read, Api) / `AddIngestion` / `AddMatching` (write, Worker). `Directory.Build.props` (shared TFM/Nullable + **`TreatWarningsAsErrors`**). Dev connection string centralised in `DbDefaults`. | anemic model is fine for a pipeline, but D3 is a real invariant worth protecting on the entity (API backfill etc. will write too); the rest is genuinely orchestration. DI split enforces the two-pipeline rule; shared props lock consistency | 2026-06-23 |
| D18 | Canonical matching | ✅ **Two-tier offline matcher** (`match` command, R3). T1: Foodstuffs NW↔PAK share `productId` → one `CanonicalProduct` per SKU (upsert by `MatchKey`, idempotent). T2: Woolworths↔Foodstuffs by **brand + normalised size (hard filter) + name-token overlap** — single clear winner ≥0.8 auto-links, else → `MatchCandidate` review queue (Pending/Approved/Rejected, persisted, never re-asked). Per-store `RawName` always kept. | no GTIN/shared-id bridges Woolworths↔Foodstuffs and names diverge wildly, so confident cases auto-link and the rest become a **review queue** (not the user spelunking the DB). Result: 3783 canonicals, 545 WW auto-linked, 760 pending | 2026-06-23 |
| D17 | Woolworths WAF backoff | ✅ Keep Woolworths at **shelf-level** crawl (finest categories) and survive the WAF rate-limit with **cooldown-and-retry + session refresh** (12/24/36s on an empty/blocked body), base delay 600ms. ~300 req/crawl. Aisle-level (≈half the requests) considered but not taken. | shelf granularity (Beef Steaks vs Mince) helps D9 canonical matching; backoff makes the high request volume reliable rather than dropping departments | 2026-06-23 |
| D25 | Canonical layer = internal join, not a catalog (target) | 🔜 **Reframe (design note: [docs/internals/canonical-model.md](docs/internals/canonical-model.md)).** A **canonical product is an internal grouping key** ("we *think* these store listings are the same item") — **never shown to a shopper, never in the shopper-facing API**; its name/description exist only as **matching features** (and future AI input). The shopper UI **always displays a real store name**, never a synthesized one (an invented title → "where's this from?"). A **canonical category is the one owned, curated, user-facing vocabulary** (the only curation surface). **Search store products, group by canonical** (search real text → full coverage incl. unmatched + real recall; dedupe via the canonical). Matching becomes **additive + identity-stable + non-destructive + reviewable** (link/unlink/**merge/split**, pointer-moves only); the canonical name becomes a **stable match anchor** the matcher links against, not a value re-minted from store data each run. Implies breaking API changes (search flip, drop canonical names from the shopper API behind an opaque item id) — sequence with Codex; admin/review surfaces still see canonical internals. | the canonical is the differentiator (same-product cross-store compare) but it's a *machine* construct: an opinion that gets corrected, not a label. Showing/curating product names confuses users and fights the pipeline; owning **category** names is intuitive and stable. Store products stay the immutable truth; the canonical is the editable overlay | 2026-06-27 |
| D22 | Canonical category (cross-store taxonomy) | ✅ **Adopt the Foodstuffs taxonomy as the shared `CanonicalCategory` tree** (Department→Aisle→Shelf), seeded by deduping NW+PAK store categories by slug-path; `StoreCategory.CanonicalCategoryId` + `CanonicalProduct.CanonicalCategoryId` map into it. New offline `CanonicalCategoryMapper` runs right after the matcher (CLI `match` + scheduled job). Foodstuffs maps by **identity** (100%), other banners by **exact (kind, slug) name** (best-effort, non-blocking); each canonical product takes the finest mapped category of its **Foodstuffs member**. Chose Foodstuffs over Woolworths because **100% of canonicals (3857/3857) already have a Foodstuffs member** → zero product remapping (Woolworths would need thousands), and its tree is smaller (~150 vs 204 shelves). Result: 193 canonical nodes, **3857/3857 products categorised**, store-cat coverage NW/PAK 100% / WW 26%. | the UI needs one shared category to browse/select by — per-store `StoreCategory` trees (D11) don't compare; a bespoke taxonomy is curation we don't need when a real, product-complete one already exists in the data | 2026-06-24 |
| D21 | Containerize the Worker | ✅ **Worker now runs in Docker** (added to compose). Final image = `mcr.microsoft.com/playwright/dotnet:v1.49.0-noble` (pinned to the `Microsoft.Playwright 1.49.0` NuGet → Chromium + browser deps pre-baked); Worker published **self-contained `-r linux-x64`** because that image ships .NET 8 while we target net9.0 (run the native apphost, not `dotnet`). Headed Chromium gets a display from **`Xvfb` started directly in `entrypoint.sh`** — **not `xvfb-run`** (hangs in non-TTY containers). `shm_size: 1gb` (Chromium /dev/shm) + `TZ: Pacific/Auckland` (cron `InTimeZone(Local)`) + `crawlarchive` volume. **Deploy target = Synology DS220+** (x86 J4025, 6GB, residential Auckland IP — better WAF/geo profile than EC2's datacenter IP), later portable to EC2. Verified: in-container headed crawl of 3 NW branches succeeded (1579/1283/2709, fresh DB writes). | the deploy host is headless, and on Synology DSM you can't `apt`-install .NET/Chromium on the host, so a container is the only practical delivery; xvfb is a property of the headless host, not the container, so containerizing adds ~no cost while giving dependency isolation + reproducibility | 2026-06-23 |
| D24 | Foodstuffs product images | ✅ **Derive the image URL from the fsimg CDN — don't fetch it.** The search API carries no image field, and the URL can't be guessed from the full SKU (that hits a generic placeholder). The real key is the productId's **numeric prefix** (before the first `-`): `5039995-KGM-000` → `https://a.fsimg.co.nz/product/retail/fan/image/400x400/5039995.png` — the exact URL the storefront's `<img>` uses (found via DevTools on the live PAK'nSAVE deals page). `FoodstuffsCrawler.ImageUrlFor` builds it at parse time → `StoreProduct.ImageUrl`. No-photo products resolve to the CDN placeholder (as on the site); UI can append `?w=N` for a resized variant. | images make the product cards usable, and this costs **zero** extra requests / no GTIN — a pure derivation from a field we already capture, vs. the alternative (a per-product detail call, ×1.5–3k/store) which would make a cheap crawl heavy | 2026-06-24 |
| D23 | Foodstuffs was-price | ✅ **Reconstruct the regular ("was") price from our own history.** NW/PAK flag a special but publish no was-price (the search API gives `singlePrice.price` + `promoId`, no original), so `StoreProduct.ApplyObservation` (the entity, D19) recovers it: when an observation is on special with no source was-price, use the prior `CurrentPrice` if the product was **off-special before** (the shelf price it dropped from) and `prior > special`, else carry the previously-reconstructed `CurrentNonSpecialPrice` forward while the special holds. Fixes both the denormalized field (→ `/deals` filter) and each `PriceSnapshot` (→ price-history `wasPrice`). **Going-forward only** — a special already running at first sighting has no recoverable was-price; chain-agnostic (only fires when the source omits it, so Woolworths is untouched). | the saving is the whole point of a deals page, and we already capture the off-special price every crawl, so the discount is recoverable from D3 history without scraping anything new; doing it in `ApplyObservation` keeps the price invariant on the entity and needs no extra query (uses in-memory current state) | 2026-06-24 |

---

## 10. API spike — Step 1 recon findings (2026-06-20)

Prior-art recon done (web research). **Decision (D2 rev): all 3 stores use Playwright, reading the page's JSON via network interception; the formal DevTools spike was skipped** (standardize over verify). Per-store notes:

| Store | Strategy | Store context | Confidence | Notes |
|---|---|---|---|---|
| Woolworths | **Playwright → intercept JSON** | lat/long | — | Standardized on Playwright-for-all (D2 rev). A JSON API exists (`api.cdx.nz`) — we read it via Playwright network interception (not HttpClient) for a uniform pattern + anti-bot cover. |
| PAK'nSAVE | **Playwright → POST edge API** (D15) | geolocation lat/long | ✅ Done | Same Foodstuffs platform as New World; one shared crawler. M1 store = **Albany** (Wairau Valley is in-store-only). |
| New World | **Playwright → POST edge API** (D15) | geolocation lat/long | ✅ Done | **Confirmed via live recon** (Worker `recon` cmd): Next.js SPA → `api-prod.newworld.co.nz/v1/edge/search/paginated/products` (Algolia, Bearer-token auth, cents pricing, embedded `categoryTrees`). |

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
