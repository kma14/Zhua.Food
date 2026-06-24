# zhua.food — Query API reference

The read-only query API for the front-end. It serves **prices, products, categories, and deals** from the
ingested data. It never crawls or migrates (those are the Worker/Migrator's job).

- **Base URL (local container):** `http://localhost:8080`
- **Format:** JSON, **camelCase** field names (ASP.NET default).
- **No `/api` prefix** — routes are top-level (`/categories`, `/products`, `/deals`). (If the front-end wants an
  `/api` prefix, say so and we add it globally.)
- **Machine-readable spec:** `GET /openapi/v1.json` (Development only).
- **Pagination:** list endpoints take `?page=1&size=20` (sizes are clamped).

---

## The data model (read this first — it explains every response)

Three layers. **Prices live only at the bottom layer**; the upper two are navigation.

```
CanonicalCategory   e.g. "Chicken Breast, Thighs & Tenders"   ← shared taxonomy tag (no price)   [D22]
   └─ CanonicalProduct   e.g. "Boneless Skinless Chicken Breast"  ← same item across stores (no price)  [D9/D18]
         ├─ StoreProduct @ PAK'nSAVE Albany   $8.99   ← a real listing at one store, with its own price
         ├─ StoreProduct @ New World Metro     $9.99
         └─ … (one StoreProduct per store that sells it)
```

- **StoreProduct** — the real, per-store listing. Has the actual price, unit price, on-special flag, raw name.
- **CanonicalProduct** — "these store listings are the same product." Holds **no price**; cheapest/compare is
  computed live as `MIN()` over its StoreProducts. This is what merges the same item across stores.
- **CanonicalCategory** — a single shared taxonomy (Department → Aisle → Shelf). A **classification tag**, not a
  price holder. Every canonical product has one; the UI groups/summarises these however it likes.

**Typical UI flow:** `GET /categories` (build nav) → list products in a category → `GET /products/{id}` (the
cross-store price compare). See [Front-end flow](#front-end-flow) below.

---

## Endpoints

### Health
| Method | Path | Returns |
|---|---|---|
| GET | `/health` | `{ "status": "ok", "service": "zhua.api" }` |
| GET | `/health/db` | `{ "db": "up" }` or `503` |

### `GET /categories` — the canonical category tree (D22)

The shared taxonomy as a nested tree, with product counts. The front-end builds its category navigation from this.

**Query:** `?kind=department` (top level only) · `?kind=aisle` (two levels) · *(omit)* = full tree incl. shelves.

**Response:** array of root nodes (Departments), each:
```json
{
  "id": "019ef85d-83ae-7098-89d3-f599782b9f01",
  "kind": "Department",                  // Department | Aisle | Shelf
  "name": "Meat, Poultry & Seafood",
  "slug": "meat-poultry-seafood",
  "path": "meat-poultry-seafood",        // full slug path; unique
  "productCount": 0,                     // canonical products directly on THIS node (usually 0 above Shelf)
  "totalProductCount": 885,              // including all descendants — use this for dept/aisle
  "children": [
    { "kind": "Aisle", "name": "Beef", "path": "meat-poultry-seafood/beef", "totalProductCount": 90, "children": [ … ] },
    { "kind": "Aisle", "name": "Chicken & Poultry", "totalProductCount": 95, "children": [ … ] }
  ]
}
```
> The 7 "general" categories the UI may want (chicken/beef/lamb/pork/eggs/milk/vegetables) are **not** baked into
> the API — they map to nodes in this tree (e.g. chicken = the `meat-poultry-seafood/chicken-poultry` aisle). The
> UI decides how to group/summarise nodes.

### `GET /products/search?q=` — search canonical products by name/brand

**Query:** `q` (required), `page`, `size`.

**Response:** `ProductSummary[]`:
```json
[{
  "id": "019ef1a2-8825-700e-bc5a-7927f3bd7e6d",
  "name": "100% Pure Goat's Milk Powder", "brand": "Healtheries", "size": "450g",
  "category": "UHT Milk & Milk Powder",     // canonical category leaf name (denormalized)
  "cheapestPrice": 37.19,                    // MIN across its stores
  "storeCount": 2,
  "onSpecialSomewhere": false
}]
```

### `GET /products/{id}` — same-product cross-store compare (the core view)

The "where's it cheapest" view: one canonical product, every store's real listing, cheapest first.

**Response:** `ProductComparison`:
```json
{
  "id": "019ef1a2-880e-737b-a6e8-bc376177a9d3",
  "name": "Boneless Skinless Chicken Breast", "brand": "Pams Free Range", "size": null,
  "category": "Chicken Breast, Thighs & Tenders",
  "cheapestPrice": 8.99,
  "saving": 1.00,                            // dearest − cheapest across stores
  "prices": [
    { "store": "PAK'nSAVE Albany", "chain": "PaknSave", "suburb": "Albany",
      "storeName": "Boneless Skinless Chicken Breast",   // the store's OWN name for this item
      "price": 8.99, "isOnSpecial": false, "nonSpecialPrice": null,
      "unitPrice": 22.48, "unitOfMeasure": "1kg", "lastSeenAt": "2026-06-24T06:01:14+00:00" },
    { "store": "New World Metro Auckland", "chain": "NewWorld", "price": 9.99, "unitPrice": 24.98, "unitOfMeasure": "1kg", … }
  ]
}
```

### `GET /deals?chain=` — current specials

Products on special now, **biggest dollar saving first**. Optional `?chain=Woolworths|NewWorld|PaknSave`.

**Response:** `DealItem[]`:
```json
[{
  "product": "woolworths nz beef eye fillet grass fed", "brand": "woolworths nz",
  "store": "Woolworths Takapuna", "chain": "Woolworths",
  "price": 63.99, "wasPrice": 78.99, "saving": 15.00,
  "unitPrice": 63.99, "unitOfMeasure": "1kg"
}]
```

### Admin — match review queue (D18)

The only **writes** the API makes (touch already-ingested data; no crawl/migrate). No auth yet — local/admin only.

| Method | Path | Notes |
|---|---|---|
| GET | `/admin/match-candidates` | pending queue, highest-confidence first (`page`,`size`). `MatchCandidateView[]` |
| POST | `/admin/match-candidates/{id}/approve` | link the product to the canonical; clears its sibling candidates |
| POST | `/admin/match-candidates/{id}/reject` | matcher won't propose this pair again |

`MatchCandidateView`: `{ id, storeProductName, brand, size, chain, price, candidateCanonical, score, reason }`.

---

## Front-end flow

| Step | UI shows | Call |
|---|---|---|
| 1 | Category navigation | `GET /categories` (`?kind=aisle` for a menu) |
| 2 | Products inside a chosen category | **see gap below** |
| 3 | Click a product → per-store prices | `GET /products/{id}` |
| — | A search box | `GET /products/search?q=` |
| — | A deals page | `GET /deals` |

### ⚠️ Known gap — "products in a category" (step 2)

There is **no endpoint yet that lists the products under a category node**. `/products/search` is name-based only.
For the category-browse flow, the recommended next endpoint is:

```
GET /categories/{id}/products?sort=unitPrice&limit=20
→ the canonical products in that category subtree, each as one merged row with its cheapest store +
  comparable (normalised) unit price. (Step 1 of the flow above; Step 3 = /products/{id} already exists.)
```

Not built yet — flag if the UI needs it and it's a small addition. Until then, the UI can navigate by search +
compare, or we build the above.

---

## Notes / gotchas for the front-end

- **`category` string vs `categoryId`:** `ProductSummary.category` / `ProductComparison.category` are the
  denormalized leaf **name** (e.g. "Chicken Breast, Thighs & Tenders"). The structured tree + ids come from
  `GET /categories`.
- **Unit prices are not yet normalised to one comparable unit.** `unitPrice` + `unitOfMeasure` are as the store
  published them (`1kg`, `100g`, `100ml`, `1L`, `1ea`…). Cross-product "cheapest by unit" needs normalisation —
  planned with the `/categories/{id}/products` endpoint above.
- **Not every StoreProduct is matched to a CanonicalProduct.** Unmatched listings still exist (with prices) but
  won't appear in same-product compare until the matcher links them.
- **Prices are NZD.** `lastSeenAt` is UTC (ISO-8601).
