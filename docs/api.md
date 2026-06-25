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

### Price dates (every priced response carries these)

| Field | Means | Show as |
|---|---|---|
| `priceAsOf` | when we **last confirmed** this price (refreshed every crawl) | "price as of 24 Jun 6am" — freshness |
| `priceUpdatedAt` | when the price **last changed** (D3); `null` if never moved | "price changed 22 Jun" |

On list/merged views (`/categories/{id}/products`, `/products?category=`) these are the **cheapest store's**
dates. `ProductSummary` (search) carries only `priceAsOf`. Prices update on the **twice-daily** crawl (6am/6pm
NZ), so `priceAsOf` is at most ~12h old.

### Filtering by store (`?storeId=`)

Product-discovery endpoints take an optional **`storeId`** filter so the UI can scope everything to the stores a
shopper actually uses (e.g. their nearby branches). The ids come from [`GET /stores`](#get-stores--the-physical-stores-we-track).

- **Repeatable** → a list: `?storeId=<a>&storeId=<b>` means "available at store **a or b**".
- Applies to **`GET /categories`**, **`GET /categories/{id}/products`** ≡ **`GET /products?category=`**, and **`GET /products/search`**.
- When set, a product is included only if it's sold at one of those stores, and **`cheapestPrice` / `storeCount` /
  `onSpecial` / counts are recomputed over just the selected stores** — not globally. This matters because
  Foodstuffs branches are **independently priced** (D16), so "cheapest at *my* PAK'nSAVE" ≠ "cheapest anywhere".
- Omit it for the all-stores view. An unknown id simply matches nothing; a malformed id → `400`.

---

## Endpoints

### Health
| Method | Path | Returns |
|---|---|---|
| GET | `/health` | `{ "status": "ok", "service": "zhua.api" }` |
| GET | `/health/db` | `{ "db": "up" }` or `503` |

### `GET /stores` — the physical stores we track

The active stores (M1 = 7). For store pickers, a map, and to qualify the store names that appear in comparisons.

**Query:** `?supermarket=Woolworths|NewWorld|PaknSave` (optional). Returns **active stores only**.

**Response:** `StoreView[]` (ordered by supermarket, then name):
```json
[{
  "id": "33333333-3333-3333-3333-333333333333",
  "supermarket": "PaknSave",
  "name": "PAK'nSAVE Albany",
  "suburb": "Albany",
  "latitude": -36.73, "longitude": 174.7067,
  "productCount": 2329,                          // priced listings we currently hold for this store
  "lastCrawledAt": "2026-06-24T10:28:00+00:00"   // last successful crawl finish (freshness); null if never crawled
}]
```
> Woolworths runs **1 active store** (Takapuna) — it's nationally priced so branches are identical (D16). The
> `store`/`supermarket`/`suburb` fields on product responses line up with these.

### `GET /categories` — the canonical category tree (D22)

The shared taxonomy as a nested tree, with product counts. The front-end builds its category navigation from this.

**Query:** `?kind=department` (top level only) · `?kind=aisle` (two levels) · *(omit)* = full tree incl. shelves.
Optional **`?storeId=`** (repeatable) — counts then reflect only products sold at those stores (see [Filtering by store](#filtering-by-store-storeid)).

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

**Query:** `q` (required), `page`, `size`, optional **`?storeId=`** (repeatable — restrict to those stores; see [Filtering by store](#filtering-by-store-storeid)).

**Response:** `ProductSummary[]`:
```json
[{
  "id": "019ef1a2-8825-700e-bc5a-7927f3bd7e6d",
  "name": "100% Pure Goat's Milk Powder", "brand": "Healtheries", "size": "450g",
  "category": "UHT Milk & Milk Powder",     // canonical category leaf name (denormalized)
  "imageUrl": "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5005182.png",  // cheapest store's image
  "cheapestPrice": 37.19,                    // MIN across its stores
  "storeCount": 2,
  "onSpecialSomewhere": false,
  "priceAsOf": "2026-06-24T06:02:46+00:00"   // cheapest store's freshness
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
  "imageUrl": "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5105651.png",  // representative (cheapest store's)
  "cheapestPrice": 8.99,
  "saving": 1.00,                            // dearest − cheapest across stores
  "prices": [
    { "store": "PAK'nSAVE Albany", "supermarket": "PaknSave", "suburb": "Albany",
      "storeName": "Boneless Skinless Chicken Breast",   // the store's OWN name for this item
      "imageUrl": "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5105651.png",  // this store's image
      "price": 8.99, "isOnSpecial": false, "nonSpecialPrice": null,
      "unitPrice": 22.48, "unitOfMeasure": "1kg",
      "priceUpdatedAt": "2026-06-22T08:08:59+00:00", "priceAsOf": "2026-06-24T06:01:44+00:00" },
    { "store": "New World Metro Auckland", "supermarket": "NewWorld", "price": 9.99, "unitPrice": 24.98, "unitOfMeasure": "1kg", … }
  ]
}
```

> **`supermarket`** = the store group (`Woolworths` | `NewWorld` | `PaknSave`). (Was `chain`.)

### `GET /products/{id}/price-history` — price over time, per store

One **step series per store** from the change-only snapshots (D3). Optional `?days=N` caps the range.

**Response:** `ProductPriceHistory`:
```json
{
  "id": "019ef1a2-880e-780a…", "name": "Mandarins", "brand": null, "size": null,
  "stores": [
    { "store": "PAK'nSAVE Albany", "supermarket": "PaknSave", "suburb": "Albany",
      "points": [
        { "date": "2026-06-22T…", "price": 3.49, "isOnSpecial": true, "wasPrice": null, "unitPrice": … },
        { "date": "2026-06-23T…", "price": 2.99, "isOnSpecial": true, "wasPrice": null, "unitPrice": … },
        { "date": "2026-06-24T…", "price": 3.49, "isOnSpecial": true, "wasPrice": null, "unitPrice": … }
      ] }
  ]
}
```
> **Render as a step line** — points are **sparse by design** (each one is a real price *change*; the price holds
> until the next point), not evenly spaced. History is **short for now** (crawling started recently) and grows
> with each twice-daily crawl. For Foodstuffs `wasPrice` is **reconstructed** from the prior shelf price (D23),
> so it's `null` only for a special we first saw already running — see the /deals note.

### `GET /categories/{id}/products` — products inside a category (D22)

The products under a category node (its **whole subtree**), each **merged across stores** and shown at its
cheapest store. This is "show me the products in this category." `id` comes from `GET /categories`.

> **Two equivalent URLs (same data):** `GET /categories/{id}/products` (sub-resource) and
> `GET /products?category={id}` (filter on the products resource). Use whichever fits the call site.

**Query:** `sort=unitPrice` (default — comparable per kg/L/ea, nulls last) `| price` (raw cheapest); `page`, `size`;
optional **`?storeId=`** (repeatable — restrict to those stores, priced within them; see [Filtering by store](#filtering-by-store-storeid)).

**Response:** `CategoryProduct[]`:
```json
[{
  "id": "019ef1a2-880e-737b-a6e8-bc376177a9d3",
  "product": "Boneless Skinless Chicken Breast", "brand": "Pams Free Range", "size": null,
  "imageUrl": "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5105651.png",  // cheapest store's image
  "originalName": "Boneless Skinless Chicken Breast",   // the cheapest store's own name
  "cheapestPrice": 8.99,
  "unitPrice": 22.48, "unit": "1kg",      // normalised comparable unit price; null if not comparable
  "storeCount": 6,
  "cheapestStore": "PAK'nSAVE Albany", "supermarket": "PaknSave",
  "onSpecialSomewhere": false,
  "priceUpdatedAt": "2026-06-22T08:08:59+00:00",   // cheapest store's: when its price last changed
  "priceAsOf": "2026-06-24T06:01:44+00:00"         // cheapest store's: when last confirmed in a crawl
}]
```
Click a row → `GET /products/{id}` for the per-store breakdown.

> **Mixed units caveat:** at a broad node (e.g. the *Beef* aisle mixes per-kg steaks with per-pack sausages),
> `unitPrice` spans `1kg` and `1ea`, so a single sorted list interleaves them — use the `unit` field to group,
> or query a **shelf** (homogeneous units) for a clean cheapest list.

### `GET /deals?supermarket=` — current specials

Products on special now, **biggest dollar saving first**. Optional `?supermarket=Woolworths|NewWorld|PaknSave`.

**Response:** `DealItem[]`:
```json
[{
  "product": "woolworths nz beef eye fillet grass fed", "brand": "woolworths nz",
  "imageUrl": "https://assets.woolworths.com.au/images/2010/67807.jpg?...&w=200&h=200",
  "store": "Woolworths Takapuna", "supermarket": "Woolworths",
  "price": 63.99, "wasPrice": 78.99, "saving": 15.00,
  "unitPrice": 63.99, "unitOfMeasure": "1kg",
  "priceUpdatedAt": "2026-06-23T18:08:56+00:00", "priceAsOf": "2026-06-24T06:00:33+00:00"
}]
```
> ⚠️ **Foodstuffs specials appear once we've seen the price drop.** NW/PAK flag a special but publish **no
> was-price**, so we *reconstruct* it from our own history — the shelf price we last recorded before the product
> went on special (D23). This is **going-forward only**: a special that was already running the first time we saw
> the product has no recoverable was-price and stays out of `/deals` until it re-prices. Woolworths publishes its
> own was-price, so it's never affected. (Early on, expect mostly Woolworths until NW/PAK specials turn over.)

### Admin — match review queue (D18)

The only **writes** the API makes (touch already-ingested data; no crawl/migrate). No auth yet — local/admin only.

| Method | Path | Notes |
|---|---|---|
| GET | `/admin/match-candidates` | pending queue, highest-confidence first (`page`,`size`). `MatchCandidateView[]` |
| POST | `/admin/match-candidates/{id}/approve` | link the product to the canonical; clears its sibling candidates |
| POST | `/admin/match-candidates/{id}/reject` | matcher won't propose this pair again |

`MatchCandidateView`: `{ id, storeProductName, brand, size, supermarket, price, candidateCanonical, score, reason }`.

---

## Front-end flow

| Step | UI shows | Call |
|---|---|---|
| 0 | Store list / picker / map | `GET /stores` |
| 1 | Category navigation | `GET /categories` (`?kind=aisle` for a menu) |
| 2 | Products inside a chosen category | `GET /categories/{id}/products` (or `GET /products?category={id}`) |
| 3 | Click a product → per-store prices | `GET /products/{id}` |
| — | Price chart for a product | `GET /products/{id}/price-history` |
| — | A search box | `GET /products/search?q=` |
| — | A deals page | `GET /deals` |

The whole flow is now backed end-to-end.

---

## Notes / gotchas for the front-end

- **`category` string vs `categoryId`:** `ProductSummary.category` / `ProductComparison.category` are the
  denormalized leaf **name** (e.g. "Chicken Breast, Thighs & Tenders"). The structured tree + ids come from
  `GET /categories`.
- **Two flavours of unit price.** On `/products/{id}` and `/deals`, `unitPrice` + `unitOfMeasure` are **as the
  store published them** (`1kg`, `100g`, `100ml`, `1L`, `1ea`…). On **`/categories/{id}/products`**, `unitPrice`
  is **normalised** to one comparable base (`unit` = `1kg`/`1L`/`1ea`) so products can be ranked by value
  (`null` when the store's unit can't be parsed).
- **Not every StoreProduct is matched to a CanonicalProduct.** Unmatched listings still exist (with prices) but
  won't appear in same-product compare until the matcher links them.
- **Product images (`imageUrl`).** Present on search, category, compare (top-level + per-store), and deals. On the
  canonical/merged views it's the **cheapest store's** image; `/products/{id}` also gives each store's own under
  `prices[].imageUrl`. Can be `null` if no store has one. Sources differ: **Woolworths** = its own CDN
  (`assets.woolworths.com.au`, already sized ~200×200); **Foodstuffs** = the `a.fsimg.co.nz` CDN at `400x400` — for
  Foodstuffs you can append **`?w=N`** for a smaller variant (e.g. `?w=200`), and imageless products resolve to a
  generic placeholder image (a real 200 response, just not a photo — same as on the supermarket's own site).
- **Prices are NZD.** `lastSeenAt` is UTC (ISO-8601).
