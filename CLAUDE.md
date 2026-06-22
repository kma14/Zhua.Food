# CLAUDE.md — working rules for zhua.food

Auckland grocery **price-intelligence** platform (information layer only — NOT e-commerce / delivery / cart / marketplace).

## Background & status

**Goal:** help Auckland shoppers find where groceries are cheapest — answering "where is X cheapest now / which store is lowest for X / is X on special / what's its price history / how much could I save shopping across stores". It's an **information layer**, not a shop, cart, or delivery service.

**M1 scope:** 3 stores (Woolworths Takapuna, New World Takapuna, **PAK'nSAVE Albany** — Wairau Valley is in-store-only), **full catalog via each store's category tree** (browse Department→Aisle→Shelf, tag each product's store category — D10), **twice-daily** crawl + price history + search/compare/deals APIs. Future: Chinese/Korean/Indian supermarkets (not M1).

**Status:** Phase 1 ingestion working — **all 3 crawlers live & verified** (Woolworths via browse-JSON D2/D10; New World + PAK'nSAVE via shared `FoodstuffsCrawler` D15). StoreCategory tree (D11), raw archive (D12), promo tags (D13) done; counts match each source; cross-banner same-product price compare confirmed. **Next:** Phase 2 scheduler (Quartz) + remaining departments + D9 canonical matching.

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
| `Zhua.Api` | Query REST API — **read-only**. |
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
- **Crawlers (D2):** Playwright for all 3 stores; **parse the page's intercepted JSON, not the DOM**. Store context = geolocation (lat/long). Cadence is config-driven, default **twice-daily** (R6/D7); crawl stores sequentially and politely. Woolworths = browse-API per category (D10); New World + PAK'nSAVE = shared `FoodstuffsCrawler` (D15), one base class, subclasses differ only by domain + departments.
- **One crawler per chain, registered in `Worker/Program.cs`** (`AddSingleton<IStoreCrawler, …>`); the orchestrator picks by `Store.Chain`. The Worker also has a throwaway `recon <url>` command (headed, dumps every JSON response + request headers/body) for reverse-engineering a new store's API.

## Dev workflow

```bash
docker compose up -d postgres                          # Postgres on host port 5433
dotnet ef database update -p src/Zhua.Infrastructure   # or: docker compose up --build migrator
dotnet build Zhua.Food.sln
dotnet test                                            # run all tests
dotnet run --project src/Zhua.Api                      # GET /health, /health/db
```

## Gotchas (learned the hard way)

- **Host Postgres is on port 5433** (5432 collides with a native PostgreSQL on this machine). In-container services use `postgres:5432` over the Docker network. **Other projects' containers on this machine also bind 5433** (e.g. `tradematch-db-uat`); if Docker restarts they can grab the port first and our `zhuafood-postgres-1` fails to bind (Exited 255). Fix: `docker stop` the squatter, then `docker compose up -d postgres`.
- **EF tools startup project = Infrastructure, not Migrator.** Infrastructure has `Design` + `ZhuaDbContextFactory`; the Migrator does not reference `Design`. Run `dotnet ef … --project src/Zhua.Infrastructure --startup-project src/Zhua.Infrastructure`.
- **Pin `Microsoft.EntityFrameworkCore.Relational` in the executable projects** (Migrator/Api/Worker). Otherwise `Design`'s version doesn't flow to them, they run on EF **9.0.1**, and the migrator **silently applies nothing** (exits 0, creates no tables). Keep all EF Core + Npgsql at **9.0.4**; target **net9.0**.
- **`dotnet run --project src/Zhua.Worker` uses the project dir as CWD**, so the crawl archive lands at `src/Zhua.Worker/crawl-archive/` (still git-ignored).
- **Foodstuffs (NW/PAK'nSAVE) edge API needs an anonymous `Authorization: Bearer` token** the SPA mints — a raw fetch with cookies alone returns empty/401. We capture it from the page's own `api-prod` requests during warmup. Also: **prices are in cents** (divide by 100); the search API exposes **no GTIN and no image URL**.
- **Foodstuffs storeId**: prefer `Store.ExternalStoreId`; else resolved at crawl time from `…/next/api/stores/geolocation?lat=&lng=` (returns nearest store — seed precise store coords). New World "Takapuna" = the **Shore City** branch.

## Conventions

- Match surrounding code style. One `IEntityTypeConfiguration<T>` per entity under `Infrastructure/Persistence/Configurations`. Entities use `required` for mandatory strings; XML-doc non-obvious choices with the plan id (e.g. "plan D3").
- Add tests when building testable code: parser golden-file fixtures for crawlers, `WebApplicationFactory` + Testcontainers for the Api.
