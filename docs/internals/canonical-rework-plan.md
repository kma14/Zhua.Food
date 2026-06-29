# zhua.food — canonical-layer rework: implementation plan

Executes the target design in [item-model.md](item-model.md) (D25), **one phase at a time**. Each phase is
independently shippable and tested. **Ordering rule:** additive/internal first, **breaking-for-Codex last and
coordinated** (the front-end builds against the live API). AI matching is *not* in this plan — it's deferred
([ai-matching.md](ai-matching.md)).

Legend: 🟢 additive/non-breaking · 🟡 coordinate with Codex · 🔴 breaking (do last).

---

## Phase 1 — `description` field 🟢 ✅ DONE (2026-06-27)

Give the canonical one owned, stable `description` (grouping label + match anchor), expose it, and **stop the matcher
overwriting it**. Fully additive — nothing removed.

- [x] Domain: add `CanonicalProduct.Description` (`string?`).
- [x] Infra: EF config + migration (`20260627024936_CanonicalProductDescription`); one-time backfill `Description = Name` (3920 rows).
- [x] Matcher: `Name`/`Description` set **only on creation**, never re-minted; new canonical seeds `Description` from the representative listing. `create-canonical` also sets it.
- [x] API: `description` added to `ProductSummary` / `ProductComparison` / `CategoryProduct`.
- [x] Tests: matcher no-overwrite (rename listing → re-run → text held); API returns `description` (search/compare/category). 87 green.
- [x] Docs: api.md (new field + grouping-label note), matching.md (no-overwrite note).

**Done:** API returns canonical `id` + `description`; re-running the matcher never changes an existing canonical's
text. Codex can adopt the grouping label now.

## Phase 2 — Search: store-first, grouped by item 🟡 ✅ DONE (2026-06-29)

Search the **real** store text, group by item. Closes the recall/coverage gap and stops forcing a synthetic name on
the UI. **Final shape (after design review):** the API returns the **group + raw listings, no aggregates** — the
client ranks them (cheapest is *not* always what the shopper wants; nearest store often matters more), and it's a
small set (≤7 stores/group).

- [x] Store-first query (`ProductQuery`): filter `Product` `RawName`/`RawBrand` (active, priced) → group by
      `ItemId ?? Id` (unmatched ⇒ a group of one) → return **every** listing in the group, not a representative.
- [x] `ProductGroup` = item metadata (`itemId` + `description` + `category`) + **`products[]`** (the payload). Each
      `ProductListing` is pure per-listing facts incl. a **server-normalised comparable `unitPrice`/`unit`** (domain
      logic stays server-side). **No** root `cheapestPrice`/`saving`/`storeCount`/`onSpecialSomewhere` — the UI derives them.
- [x] **One `/products` collection:** `GET /products?q=&category=&storeId=&page=&size=` (dropped `?sort=` — the UI
      sorts the small list); **dropped `/products/search`** and the bare-`/products`-400 wart. `GET /products/{id}`
      (a product id) returns the **`ProductGroup` it belongs to**. `/categories/{id}/products` = the browse alias.
- [x] **Controller merge:** `StoreProductsController` deleted — admin link is now `PATCH /products/{id}`. 3
      product-ish controllers → 2 (`ProductsController` + `ItemsController`).
- [x] Tests + api.md rewritten. 96 green.
- [ ] **Deferred (evolve as required):** `Cache-Control`/`ETag` on these reads (read-mostly, twice-daily updates →
      high cache hit-rate); SQL-side grouping + rate-limiting if the catalogue/store count grows.

**Breaking for Codex** — shopper search/compare flipped to the grouped shape; bundled with the rename's breaking
wave (2026-06-29).

## Phase 3 — Category CRUD (the curation surface) 🟢 ✅ DONE (2026-06-27)

Make the canonical **category** an owned, editable vocabulary — the *only* curation surface.

- [x] `CanonicalCategory.IsArchived` + migration (`CanonicalCategoryArchive`). **Delete = soft-archive** (chosen: the
      mapper regenerates Foodstuffs nodes, so a hard delete is futile).
- [x] Curation writes on the **`/categories`** resource (one controller, reads public + writes `[Authorize("Admin")]`):
      `POST /categories` (create, derived slug/path, 400/404/409), `PATCH /{id}` (rename display name only — path/slug
      stay as the stable mapper key), `DELETE /{id}` (cascade soft-archive). *(Reparent/move deferred — changes `Path`,
      would duplicate against the mapper.)*
- [x] Reads exclude archived (tree + category-products → archived id = `404`).
- [x] Mapper respects archived: reuses by `Path` so it never un-archives or duplicates; `FinestMappedCategory` skips
      archived so products bubble to the nearest live ancestor.
- [x] Tests: 8 API CRUD (create/rename/archive + 400/404/409) + mapper archived-stays-archived-and-bubbles. 96 green.
- [x] Docs: api.md (category-curation admin section).

Independent of Phases 1–2.

## Phase 4 — Correction toolkit: merge (unlink/split already done) 🟢

The pointer-move operations a "we *think*" overlay needs (non-destructive; never touch store listings).

- [x] **Unlink** — folded into `PATCH /store-products/{id}` `{ "canonicalProductId": null }` (shipped with the RESTful
      admin refactor, see note below).
- [ ] `POST /canonicals/{id}/merge { intoId }` — *or* a RESTful equivalent (repoint members + candidates, then remove
      the empty one; decide price-history handling). The one genuinely-verb-shaped action left; model as a `merge`
      sub-resource or a `PATCH` that repoints, TBD when built.
- [x] **Split** — already covered: move a member out via `PATCH /store-products/{id}` (relink) or `POST
      /canonical-products` + relink.
- [ ] Matcher must respect merges (a merged-away canonical isn't recreated — `MatchKey` reconciliation).
- [ ] Tests + api.md.

> **RESTful admin refactor (2026-06-27):** the admin surface was de-verbed and de-`/admin/`-prefixed. `*AdminController`
> classes are gone; each mutation lives on the resource it changes, guarded by `[Authorize("Admin")]`:
> `PATCH /match-candidates/{id}` (approve/reject via `status`), `PATCH /store-products/{id}` (link/unlink/relink),
> `POST /canonical-products` (create). Breaking for Codex's review UI — coordinated.

## Phase 5 — Remove canonical name from the shopper API 🔴 (last)

After Codex has migrated to *real store name + `description`*, drop the synthetic canonical name from shopper
responses.

- [ ] Remove/repurpose canonical-name fields in `ProductSummary` / `ProductComparison` / `CategoryProduct`.
- [ ] Optionally drop the now-unused `CanonicalProduct.Name` column (or leave internal).
- [ ] Tests + api.md.

**Breaking — do only once the front-end no longer reads canonical name.**

---

## Sequencing & dependencies

```
Phase 1 (description) ──► Phase 2 (search) ──► Phase 5 (remove name)   [the breaking spine]
Phase 3 (category CRUD)   ─ independent ─
Phase 4 (unlink/merge/split) ─ independent (light dep on 1) ─
```

Recommended order: **1 → 3 → 4 → 2 → 5** (get all the additive value + the manual correction toolkit in first;
do the two Codex-coordinated/breaking steps last). Adjust per Codex's front-end progress.

Each phase: branch off, migration via `Infrastructure`, build clean (warnings = errors), ingestion + Api tests green,
redeploy the `api` container, update api.md, commit.
