# zhua.food — canonical-layer rework: implementation plan

Executes the target design in [canonical-model.md](canonical-model.md) (D25), **one phase at a time**. Each phase is
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

## Phase 2 — Search: store-first, grouped by canonical 🟡

Search the **real** store text, collapse by canonical. Closes the recall/coverage gap and stops forcing a synthetic
name on the UI.

- [ ] Rewrite product search: query `StoreProduct` `RawName`/`RawBrand` (active stores) → group by
      `CanonicalProductId` (null ⇒ a group of one).
- [ ] Each row: representative (cheapest) **real store name** as title + canonical `id` + `description` + cheapest
      price + store count + image + on-special; carry a `matched` flag and the right drill-in id (canonical id when
      matched, else representative store-product id).
- [ ] **REST cleanup of `/products` (agreed 2026-06-27):** collapse search + category-filter into **one filtered
      collection** — `GET /products?q=&category=&storeId=&sort=&page=&size=`, **one** list DTO; **drop
      `/products/search`** (verb-in-path) and the "bare `/products` 400s without `?category=`" wart. Keep
      `/products/{id}` + `/price-history`; `/categories/{id}/products` stays as the sub-resource alias.
- [ ] Tests: a term in only one store's wording is found; duplicates collapse; unmatched listings appear.
- [ ] Docs: api.md `/products` section + the store-first note.

**Coordinate with Codex** — response shape + the search route change. (Option: keep the old fields populated during a transition.)

## Phase 3 — Category CRUD (the curation surface) 🟢

Make the canonical **category** an owned, editable vocabulary — the *only* curation surface.

- [ ] `/admin/categories`: `POST` (create / set parent), `PATCH /{id}` (rename, move), `DELETE /{id}`.
- [ ] Delete/rename policy: reparent-children-or-block; unlink-products-or-block. Rename is already mapper-safe
      (upsert by `Path`, name set on create only); decide delete vs. an `archived` flag so the mapper can't
      regenerate a deliberately removed node.
- [ ] Mapper: respect manually-added/renamed/archived nodes.
- [ ] Tests + api.md.

Independent of Phases 1–2 — can slot in whenever.

## Phase 4 — Correction toolkit: unlink / merge / split 🟢

The pointer-move operations a "we *think*" overlay needs (non-destructive; never touch store listings).

- [ ] `POST /admin/store-products/{id}/unlink` (clear `CanonicalProductId`).
- [ ] `POST /admin/canonicals/{id}/merge { intoId }` (repoint members + candidates, then remove the empty one;
      decide price-history handling).
- [ ] Split = already covered by `link-canonical` / `create-canonical` (move a member out) — add a convenience only
      if needed.
- [ ] Matcher must respect merges (a merged-away canonical isn't recreated — `MatchKey` reconciliation).
- [ ] Tests + api.md.

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
