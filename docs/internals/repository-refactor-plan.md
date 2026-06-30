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

1. ✅ **DTO split** → `Dtos/`, one record per file. Pure move, no behaviour change.
2. ✅ **Domain `Repositories/` ports + `IUnitOfWork`** + Infra `Repositories/` implementations.
3. ✅ **Moved every service Infra → Application** onto its repo (Stores/Health → Categories → Products/Deals →
   Items/MatchReview). `Infrastructure/Services/` deleted. Rich-domain methods folded in: `Category.Create/Rename/
   Archive/Slugify` (the matcher now reuses `Category.Slugify`).
4. ✅ **Arch test** `Domain_repository_ports_stay_EF_free` — the ports can't grow an `IQueryable`/`DbContext`
   dependency. (The existing 3 boundary tests already confine EF to Infrastructure.)
5. ✅ **matcher + `CategoryMapper` → Application** over a new `IMatchingRepository` (Domain port) + `IUnitOfWork`;
   the same-item scoring extracted to a Domain service `IItemMatchingPolicy` (`HeuristicItemMatchingPolicy`), and
   `ProductNormalizer` moved to `Domain/Matching`. **`Infrastructure/Matching/` deleted — Infrastructure now has zero
   business logic** (only `Repositories/` EF adapters + `Persistence/` + `Crawling/` Playwright drivers). Matcher tests
   repointed to construct over the repo/policy. (One fix: the matcher now resolves items by id, so a newly-created
   Tier-1 item gets its `Id` assigned on creation — the old code held entity references.)

**Done 2026-06-30. 106 tests green; API contract unchanged.** (`CrawlOrchestrator` stays in Infrastructure by
design — it drives Playwright + the raw archive, a genuine infrastructure concern, not request/read logic.)

### Pragmatic deviations (flagged, not silent)
- **`Product.Saving` not added as an entity property** — a computed get-only property needs an EF `Ignore`; the saving
  is trivial arithmetic, so it stays inline in `DealQueries`. Revisit if more places need it.
- **`UnitPriceNormalizer` left in `Application/Pricing`** (not moved to Domain) — it's a pure calc used only by
  `ProductService`; moving it is cosmetic and would churn its test. Deferred.
- **Merge stays orchestrated in `ItemService`** (not an `Item.MergeInto` throwing method) — merge spans Item +
  Product + MatchCandidate, and a throwing invariant fights the `Result` 400/404/409 contract. Consistent with the
  plan's own "cross-aggregate orchestration stays in the service" rule. The per-entity behaviour that *does* fit
  (`MatchCandidate.Approve/Reject`) already lives on the entity.
