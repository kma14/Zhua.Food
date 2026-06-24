# CLAUDE.md — working rules for zhua.food

Auckland grocery **price-intelligence** platform (information layer only — NOT e-commerce / delivery / cart / marketplace).

## Background & status

**Goal:** help Auckland shoppers find where groceries are cheapest — answering "where is X cheapest now / which store is lowest for X / is X on special / what's its price history / how much could I save shopping across stores". It's an **information layer**, not a shop, cart, or delivery service.

**M1 scope:** 9 stores seeded, **7 active** — Woolworths runs **1 store** (Takapuna; it's national-priced so branches are identical — D16), Foodstuffs runs 3 each (New World Metro/Shore City/Browns Bay; PAK'nSAVE Albany/Botany/Highland Park — independently priced). Departments (expanding): Meat/Poultry/Seafood + Fruit&Veg + Fridge/Deli + Frozen. **Catalog via each store's category tree** (D10/D11), **twice-daily** crawl + price history + search/compare/deals APIs. Future: Chinese/Korean/Indian supermarkets (not M1).

**Status:** Phase 1 ingestion working — **all 3 crawlers live & verified across 9 stores** (Woolworths via browse-JSON D2/D10; New World + PAK'nSAVE via shared `FoodstuffsCrawler` D15). StoreCategory tree (D11), raw archive (D12), promo tags (D13) done; counts match each source. Cross-banner AND cross-branch same-product price compare confirmed (measured branch variation: Woolworths 0% / NW 40% / PAK'nSAVE 49% — D16). **Canonical matching done (D18):** offline `match` command groups Foodstuffs by shared productId + bridges to Woolworths by brand+size+name, auto-linking confident matches and queueing the rest for review (`MatchCandidate`). **Query API done (D20):** `/products/search`, `/products/{id}` (same-product compare), `/deals`, `/admin/match-candidates` (+approve/reject). **Quartz scheduler done (D4/D7):** Worker no-args = cron-driven crawl+match. **Worker containerized & verified (D21):** Playwright image + self-contained net9 + direct `Xvfb` entrypoint runs headed Chromium on a headless host (deploy target = Synology DS220+ → later EC2). **Canonical category done (D22):** shared cross-store `CanonicalCategory` tree (seeded from the Foodstuffs taxonomy); offline `CanonicalCategoryMapper` runs after the matcher and categorises 3857/3857 canonical products for UI browse/select. **Next:** category API endpoints (for the `zhua.web` front-end) + Api integration tests.

📋 **Full background, all decisions (D1–D9) and the phased roadmap live in [plan-cc.md](plan-cc.md)** — that's the source of truth; read it before non-trivial work and keep it updated as decisions change. (Linked rather than `@import`-ed, to keep each session's context lean — ask if you'd prefer the whole plan auto-loaded every session.)

## Prior art

A similar NZ price tracker already exists — **kiwiprice.xyz** (GitHub `Jason-nzd`). Study these as references (don't depend on them; check licenses before copying): `Jason-nzd/pakn-scraper` (**.NET + Playwright** — our exact stack), `Jason-nzd/countdown-scraper`, `Jason-nzd/supermarket-prices-nextjs`.

**Our differentiator:** real **canonical same-product cross-store comparison** (D9) + a focused, task-first UX. kiwiprice only sorts a whole category by `$/kg`, which is meaningless (it mixes whole chicken vs breast vs drumsticks) and shows the same product at two stores as separate cards — exactly what we improve on. Full recon: plan-cc.md §10.

## Architecture — Clean Architecture; respect the dependency direction

```
Domain  ←  Application  ←  { Infrastructure, Crawling }  ←  { Api, Worker, Migrator }
```

| Project | Role |
|---|---|
| `Zhua.Domain` | Entities + enums. No external deps. |
| `Zhua.Application` | Use cases + service/repository interfaces. |
| `Zhua.Infrastructure` | EF Core `DbContext`, configs, migrations, store seed. |
| `Zhua.Crawling` | Per-store crawlers. |
| `Zhua.Api` | Query REST API (read) + admin match-review writes (D20). Never crawls or migrates. **Front-end API reference: [docs/api.md](docs/api.md).** |
| `Zhua.Worker` | Ingestion — Quartz schedule + crawlers. |
| `Zhua.Migrator` | One-shot migration runner. |
| `tests/Zhua.Ingestion.Tests` | Ingestion / `CrawlOrchestrator` tests (EF InMemory). |

## Hard rules (do not violate)

- **Two pipelines stay separate.** The query `Api` **never** triggers crawling and **never** migrates the DB.
- **Migrations:** change an entity → `dotnet ef migrations add <Name> --project src/Zhua.Infrastructure --startup-project src/Zhua.Infrastructure` (Infrastructure holds `Design` + a design-time `ZhuaDbContextFactory`; the **Migrator does NOT reference Design**, so it can't be the EF-tools startup project). Only the `Migrator` (or `dotnet ef`) applies them; Api/Worker must not auto-migrate (D5).
- **Price history is change-only (D3):** append a `PriceSnapshot` only when the tuple `{Price, IsOnSpecial, NonSpecialPrice, UnitPrice}` changes; every crawl still refreshes `StoreProduct` current price + `LastSeenAt`.
- **Categories are first-class (D11):** crawl the store's own tree (Department→Aisle→Shelf), auto-discover aisles/shelves from `dasFacets`, and link products **many-to-many** to `StoreCategory` — never a denormalized category string. Category links accumulate across a crawl (a product sits under several shelves).
- **Promo tags (D13):** capture the source's promo badge into the `ProductTag` m2m dimension (chain-scoped). Tags are **volatile → reset every crawl** (NOT in the D3 price tuple, NOT snapshotted). Keep `IsOnSpecial`+was-price as the real discount signal. Woolworths "Low Price" = `tagType:"IsGreatPrice"`; "Other" is dropped.
- **Raw archive (D12):** crawlers archive every raw response to disk (default-on, 7-day self-pruning). Don't remove it — the parsed DB keeps only mapped fields, so the archive is the only way to recover/debug source data. Dir is git-ignored (`crawl-archive/`).
- **Canonical matching (D9) is a core feature, done offline (R3):** `StoreProduct.CanonicalProductId` is nullable and matching never blocks ingestion. GTIN-first — capture `Gtin` at crawl time. `Category` must be **fine-grained** ("Chicken Breast", not "Chicken").
- **Canonical category (D22) is the cross-store browse taxonomy.** `StoreCategory` (D11) is **per-store** (each banner's own tree) and trees don't compare across stores; `CanonicalCategory` is the **single shared tree** the UI selects/filters by. It's seeded from the **Foodstuffs taxonomy** (NW+PAK share it → covers every canonical product, since every canonical has a Foodstuffs member). `CanonicalCategoryMapper` (offline, in `AddMatching`) runs **right after the matcher** in both the `match` CLI and the scheduled `IngestionJob` — keep that order (it needs canonicals to exist). Foodstuffs maps by identity, other banners by exact (kind, slug) name (best-effort); `CanonicalProduct.Category` stays as the denormalized leaf-name display, `CanonicalCategoryId` is the structured link.
- **Crawlers (D2):** Playwright for all 3 stores; **parse the page's intercepted JSON, not the DOM**. Store context = geolocation (lat/long). Cadence is config-driven, default **twice-daily** (R6/D7); crawl stores sequentially and politely. Woolworths = browse-API per category (D10); New World + PAK'nSAVE = shared `FoodstuffsCrawler` (D15), one base class, subclasses differ only by domain + departments.
- **One crawler per chain, registered in `Worker/Program.cs`** (`AddSingleton<IStoreCrawler, …>`); the orchestrator picks by `Store.Chain`. The Worker also has a throwaway `recon <url>` command (headed, dumps every JSON response + request headers/body) for reverse-engineering a new store's API.

## Dev workflow

```bash
docker compose up -d postgres                          # Postgres on host port 5433
dotnet ef database update -p src/Zhua.Infrastructure   # or: docker compose up --build migrator
dotnet build Zhua.Food.sln
dotnet test                                            # run all tests
dotnet run --project src/Zhua.Worker                   # scheduled mode (Quartz cron: crawl all + match; D4/D7)
dotnet run --project src/Zhua.Worker -- crawl          # one-shot: ingest all active stores (headed Playwright)
dotnet run --project src/Zhua.Worker -- match          # one-shot: offline canonical matching (D18)
dotnet run --project src/Zhua.Api                      # GET /health, /health/db
```

## Gotchas (learned the hard way)

- **Host Postgres is on port 5433** (5432 collides with a native PostgreSQL on this machine). In-container services use `postgres:5432` over the Docker network. **Other projects' containers on this machine also bind 5433** (e.g. `tradematch-db-uat`); if Docker restarts they can grab the port first and our `zhuafood-postgres-1` fails to bind (Exited 255). Fix: `docker stop` the squatter, then `docker compose up -d postgres`.
- **EF tools startup project = Infrastructure, not Migrator.** Infrastructure has `Design` + `ZhuaDbContextFactory`; the Migrator does not reference `Design`. Run `dotnet ef … --project src/Zhua.Infrastructure --startup-project src/Zhua.Infrastructure`.
- **Pin `Microsoft.EntityFrameworkCore.Relational` in the executable projects** (Migrator/Api/Worker). Otherwise `Design`'s version doesn't flow to them, they run on EF **9.0.1**, and the migrator **silently applies nothing** (exits 0, creates no tables). Keep all EF Core + Npgsql at **9.0.4**; target **net9.0**.
- **`dotnet run --project src/Zhua.Worker` uses the project dir as CWD**, so the crawl archive lands at `src/Zhua.Worker/crawl-archive/` (still git-ignored).
- **Foodstuffs (NW/PAK'nSAVE) edge API needs an anonymous `Authorization: Bearer` token** the SPA mints — a raw fetch with cookies alone returns empty/401. We capture it from the page's own `api-prod` requests during warmup. Also: **prices are in cents** (divide by 100); the search API exposes **no GTIN and no image URL**.
- **Foodstuffs storeId**: prefer `Store.ExternalStoreId`; else resolved at crawl time from `…/next/api/stores/geolocation?lat=&lng=` (returns nearest store — seed precise store coords). New World "Takapuna" = the **Shore City** branch.
- **Compose = `postgres` + `migrator` (one-shot) + `api` (`:8080`) + `worker`** (all wait for migrator). **Dockerfiles must `COPY Directory.Build.props` before restore** (D19) or the in-container build has no TargetFramework.
- **Worker IS containerized now (D21)** and that's the deploy target (Synology DS220+ → later EC2). It drives a *headed* browser (headless is WAF-blocked, D2/D17), and a container/headless host has no display, so three things had to line up — all learned the hard way:
  - **`Xvfb` gives the headed browser an in-memory display.** Start it **directly** in `src/Zhua.Worker/entrypoint.sh` (`Xvfb :99 … & export DISPLAY=:99`), **NOT via `xvfb-run`** — `xvfb-run` hangs forever in a non-TTY container (no output, no chromium, idle). Entry needs `xvfb` **+ `x11-utils`** (for the `xdpyinfo` readiness check). Strip CRLF + `chmod +x` the script in the Dockerfile (Windows-authored).
  - **Publish the Worker SELF-CONTAINED `-r linux-x64`.** The Playwright image (`mcr.microsoft.com/playwright/dotnet:v1.49.0-noble`, pinned to match the `Microsoft.Playwright 1.49.0` NuGet → Chromium 1.49.0 + browser deps pre-baked) ships **.NET 8**, but we target net9.0. Self-contained bundles .NET 9 and runs the native `./Zhua.Worker` apphost (NOT `dotnet …`, which hits the image's .NET 8 and fails to launch).
  - **`shm_size: 1gb`** in compose (Chromium crashes on Docker's default 64MB `/dev/shm` mid-crawl) and **`TZ: Pacific/Auckland`** (the Quartz cron uses `InTimeZone(Local)`, so Local must be Auckland for 06:00/18:00 to be real). Raw archive (D12) → named volume `crawlarchive`.
  - **Verified**: in-container headed crawl of all 3 New World branches succeeded end-to-end (1579/1283/2709 products, fresh DB writes).
- **A single store's crawl failure must not kill the scheduled run.** `CrawlRun.ErrorMessage` is `varchar(2000)`, but a Playwright launch error carries a full call-log (well over 2000 chars). Storing `ex.Message` raw makes the *Failed-status save itself* throw `22001 value too long`, which orphans the run at "Running" and crashes the whole Quartz job (stores 2–7 + matcher skipped). Fix in place: `CrawlOrchestrator` **truncates** `ErrorMessage` to 2000; `IngestionJob` also wraps each store in try/catch. Root trigger was a browser launch failing right after the **host resumed from sleep** — which is also why an always-on host (the NAS) is the deploy target.
- **Woolworths is ~10× more requests than Foodstuffs** (~300 vs ~40/store): its products don't carry their category, so we must query every aisle+shelf to build the tree, while Foodstuffs queries per-department (products carry `categoryTrees`). This volume trips Woolworths' WAF rate-limit, so `FetchBrowseAsync` does **cooldown-and-retry with homepage session refresh** (12/24/36s) on a block (empty body). Don't remove that backoff or Woolworths crawls die partway through.

## Conventions

- Match surrounding code style. One `IEntityTypeConfiguration<T>` per entity under `Infrastructure/Persistence/Configurations`. Entities use `required` for mandatory strings; XML-doc non-obvious choices with the plan id (e.g. "plan D3").
- Add tests when building testable code: parser golden-file fixtures for crawlers, `WebApplicationFactory` + Testcontainers for the Api.
- **Shared build settings live in `Directory.Build.props`** (TFM, Nullable, ImplicitUsings, `TreatWarningsAsErrors`) — don't re-add them per-csproj. Warnings are errors; keep the build clean.
- **Entity invariants live on the entity** (D19): the D3 price rule is `StoreProduct.ApplyObservation` — don't re-inline price-change logic in the orchestrator. Use-case orchestration (category/tag linking, run lifecycle) stays in `CrawlOrchestrator`.
- **DI is split by pipeline** (D19): the read-only Api calls `AddPersistence` only; the Worker calls `AddPersistence().AddIngestion().AddMatching()`. Don't give the Api ingestion/matching services. Dev connection string = `DbDefaults.DevConnectionString`.
