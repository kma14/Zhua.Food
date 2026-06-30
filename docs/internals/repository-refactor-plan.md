# zhua.food — repository-pattern + rich-domain refactor plan

De-hollows the Application layer (which had become "interfaces + DTOs only, all logic in Infrastructure") into a
clean DDD/Onion shape: a **rich domain**, **repository interfaces in Domain**, **use-case services in Application**,
**EF implementations in Infrastructure**.

## Provenance (read this first)

This doc separates **decisions the user (Kevin) actually made** from descriptive notes. A long-standing problem here
is that LLM-summarised prose drifts into CLAUDE.md/plan docs and later reads as user-mandated law. So:

- 🧑‍⚖️ **= user-instructed** (a real decision Kevin made, dated). Treat as a rule.
- everything else = descriptive / implementation detail — change it freely if the code says otherwise.

## Decisions

- 🧑‍⚖️ **Rich domain model** — core business logic lives *on* the domain objects (methods on entities + domain
  services), not in services elsewhere, so callers can't get it wrong. *(Kevin, 2026-06-30)*
- 🧑‍⚖️ **Repository interfaces live in `Zhua.Domain`** (not Application) — the domain owns both its aggregates and the
  contract to persist them; the interface is shared by Application (calls it) and Infrastructure (implements it). Keep
  Application = services, not repository ports. *(Kevin, 2026-06-30)*
- 🧑‍⚖️ **No query ports; reads project in the service, in memory.** A Domain repository may return only **domain types +
  primitives** (never an Application DTO — the arch test forbids Domain→outward). So repositories return **filtered
  entities** (the `WHERE`/`COUNT` still runs at the DB — only matching rows load) and **primitive aggregates**
  (`IReadOnlyDictionary<Guid,int>` for counts); the **Application service shapes the final DTO in memory**. Trade-off
  accepted: the entity→DTO projection runs in the service rather than in SQL. Fine at current scale (a few thousand
  rows; DB-side filtering + counts intact). **Watch this** as the catalogue grows — if it bites, add a dedicated
  read/CQRS path then (the "SQL-side projection later" deferred item). *(Kevin, 2026-06-30 — explicitly flagged to
  keep an eye on.)*
- 🧑‍⚖️ **DTOs split into a `Dtos/` folder per feature, one record per file** (retire the `*Contracts.cs` dumping
  ground). *(Kevin, 2026-06-30)*
- Thin service interfaces (`IProductService` etc.) are **kept** as a DI seam for the Api (optional under Clean
  Architecture, but cheap and zero Api churn). Descriptive, not a hard rule.

## Target structure

```
Zhua.Domain/
  Entities/        rich entities (behaviour as concrete methods — see "domain methods" below)
  ValueObjects/    ProductObservation · UnitPriceNormalizer (moved in: pure pricing knowledge)
  Enums/
  Repositories/    IItemRepository · IProductRepository · ICategoryRepository · IMatchCandidateRepository · IUnitOfWork
  Services/        IItemMatchingPolicy (domain service — added with the matcher phase)

Zhua.Application/
  Common/Result.cs
  {Feature}/
    {Feature}Service.cs      concrete use case = all orchestration/logic (depends on Domain repos + IUnitOfWork)
    I{Feature}Service.cs     thin DI seam the Api depends on
    Dtos/                    one record per file
  Crawling/ Matching/        existing port interfaces (unchanged this round)

Zhua.Infrastructure/
  Persistence/   ZhuaDbContext · UnitOfWork.cs · Configurations/ · Migrations/
  Repositories/  ItemRepository · ProductRepository · CategoryRepository · MatchCandidateRepository
                 StoreRepository · DealRepository · HealthRepository      (replaces Services/)
```

## Rich-domain methods to fold in (as each feature is touched)

Precedent already in the code: `Product.ApplyObservation` (D3/D19), `MatchCandidate.Approve/Reject`.

| Add | Onto | Replaces loose logic in |
|---|---|---|
| `Item.MergeInto(survivor)` (self/cycle/already-merged guard + set `MergedIntoId`) | `Item` | `ItemService.MergeAsync` |
| `Category.CreateChild(kind, name)` / `.Rename(name)` / `.Archive()` (slug/path derivation, invariants) | `Category` | `CategoryService` |
| `Product.Saving` (computed: `NonSpecialPrice − Price` when on special) | `Product` | `DealQueries` projection |
| `UnitPriceNormalizer.ToComparable` (move project → Domain) | `Domain/ValueObjects` | `Application/Pricing` |
| same-item scoring (`ProductNormalizer` + token overlap + thresholds) | `IItemMatchingPolicy` domain service | `ItemMatcher` (matcher phase) |

Cross-aggregate orchestration (repoint an item's products, cascade archive a subtree, bulk regroup) stays in the
**Application service / domain service** — it spans aggregates, so it doesn't belong on a single entity.

## Execution order (each step: build clean + all tests green before the next)

1. **DTO split** → `Dtos/`, one record per file. Pure move, no behaviour change.
2. **Domain `Repositories/` interfaces + `IUnitOfWork`** + Infra `Repositories/` implementations; add rich-domain
   methods as each entity is touched.
3. **Move each service Infra → Application** onto its repo: Stores/Deals/Health → Products → Categories →
   Items/MatchReview.
4. **Arch test**: EF / `ZhuaDbContext` used **only** under `Zhua.Infrastructure`.
5. *(optional)* matcher + `CategoryMapper` → Application + `IItemMatchingPolicy`.

Keeps the existing 3 architecture tests green throughout (they already enforce Api↛Infra/EF, Application↛Infra/EF,
Domain↛outward — the last one is what makes "repository interfaces in Domain" safe).
