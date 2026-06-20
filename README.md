# zhua.food

Auckland grocery **price-intelligence** platform (information layer only — not e-commerce).
Architecture, decisions and roadmap live in **[plan-cc.md](plan-cc.md)**.

## Solution layout (Phase 0 skeleton)

| Project | Role |
|---|---|
| `src/Zhua.Domain` | Entities + enums (no dependencies) |
| `src/Zhua.Application` | Use cases / service + repository interfaces (Phase 4) |
| `src/Zhua.Infrastructure` | EF Core `DbContext`, configurations, migrations, store seed |
| `src/Zhua.Crawling` | Per-store crawler seam — Playwright + JSON interception (Phase 1) |
| `src/Zhua.Api` | Query REST API (health endpoints today; search/compare/deals in Phase 4) |
| `src/Zhua.Worker` | Ingestion worker — Quartz schedule + crawlers (Phase 2) |
| `src/Zhua.Migrator` | One-shot migration runner (plan D5) |

## Run locally

Prereqs: **.NET 9 SDK** and **Docker**.

```bash
# 1. Start Postgres
docker compose up -d postgres

# 2. Apply the schema (uses ConnectionStrings__Default, else localhost default)
dotnet ef database update --project src/Zhua.Infrastructure

# 3. Run the API
dotnet run --project src/Zhua.Api
#   GET /health      -> { status: "ok" }
#   GET /health/db   -> { db: "up" }   (verifies DB connectivity)
```

To apply migrations the way Compose does (build + run the one-shot migrator):

```bash
docker compose up --build migrator
```

## Database

- Connection string: env `ConnectionStrings__Default` (or `ConnectionStrings:Default` in Api `appsettings.json`).
- Compose dev credentials: user `zhua` / password `zhua` / database `zhua`.
- Migrations: `src/Zhua.Infrastructure/Persistence/Migrations`.
- Seed: the 3 Milestone-1 stores (Woolworths Takapuna, New World Takapuna, PAK'nSAVE Glenfield) with geolocation.
