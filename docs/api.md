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
Category   e.g. "Chicken Breast, Thighs & Tenders"   ← shared taxonomy tag (no price)   [D22]
   └─ Item   e.g. "Boneless Skinless Chicken Breast"  ← same item across stores (no price)  [D9/D18]
         ├─ Product @ PAK'nSAVE Albany   $8.99   ← a real listing at one store, with its own price
         ├─ Product @ New World Metro     $9.99
         └─ … (one Product per store that sells it)
```

- **Product** — the real, per-store listing. Has the actual price, unit price, on-special flag, raw name.
- **Item** — "these store listings are the same product." Holds **no price**; cheapest/compare is
  computed live as `MIN()` over its Products. This is what merges the same item across stores.
- **Category** — a single shared taxonomy (Department → Aisle → Shelf). A **classification tag**, not a
  price holder. Every item has one; the UI groups/summarises these however it likes.

**Typical UI flow:** `GET /categories` (build nav) → list products in a category → `GET /products/{id}` (the
cross-store price compare). See [Front-end flow](#front-end-flow) below.

### `description` — the grouping label (D25)

Each `ProductGroup` carries a **`description`** at its root — our **owned grouping label**, the honest "we think
these N store listings are the same: *X*" phrase, **not** a store's title. It's the internal item's caption; today
seeded from the item, over time curated (see [internals/item-model.md](internals/item-model.md)). **Front-end
guidance:** each listing's **`name` is already a real store name** — use it as the title; render `description` as
the *grouping caption*, never the title. `null` for an unmatched listing (a group of one).

### Price dates (every priced response carries these)

| Field | Means | Show as |
|---|---|---|
| `priceAsOf` | when we **last confirmed** this price (refreshed every crawl) | "price as of 24 Jun 6am" — freshness |
| `priceUpdatedAt` | when the price **last changed** (D3); `null` if never moved | "price changed 22 Jun" |

Every listing inside `products[]` carries its own pair. Prices update on the **twice-daily** crawl (6am/6pm NZ), so
`priceAsOf` is at most ~12h old.

### Filtering by store (`?storeId=`)

Product-discovery endpoints take an optional **`storeId`** filter so the UI can scope everything to the stores a
shopper actually uses (e.g. their nearby branches). The ids come from [`GET /stores`](#get-stores--the-physical-stores-we-track).

- **Repeatable** → a list: `?storeId=<a>&storeId=<b>` means "available at store **a or b**".
- Applies to **`GET /categories`** and **`GET /products`** (incl. its `?category=` / `/categories/{id}/products` browse alias).
- When set, a group's **`products[]` is restricted to just those stores** (and groups with none drop out), so any
  cheapest/count the client derives is over the selected stores — not globally. This matters because Foodstuffs
  branches are **independently priced** (D16), so "cheapest at *my* PAK'nSAVE" ≠ "cheapest anywhere".
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

### `GET /categories` — the category tree (D22)

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
  "productCount": 0,                     // items directly on THIS node (usually 0 above Shelf)
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

### `GET /products` — the product collection (search + browse)

Searches/filters the **real store listings** and **groups them by item** so the same product across stores comes
back as **one group with all its listings**. The API computes **no** cheapest/saving/count — the `products[]` list
is the payload and **the client ranks it** (cheapest, nearest, on-special). Unmatched listings are a group of one.

**Query (all optional):** `q` (search the real store name/brand) · `category={id}` (a category node — its whole
subtree) · `storeId=` (repeatable) · `page` · `size`. No filter ⇒ the whole catalogue, paged. `category` returns
only matched listings (the item carries the category); an unknown/archived `category` → `404`.

**Response:** `ProductGroup[]`:
```json
[{
  "itemId": "019ef1a2-880e-737b-a6e8-bc376177a9d3", // internal grouping id; null = an unmatched listing
  "description": "Beef Mince 1kg",                  // item caption (D25); client decides usage — see note
  "category": "Beef Mince",                         // item category leaf (denormalized); null if unmatched
  "products": [                                     // THE payload — every store's listing; the client ranks them
    { "id": "dddd…00a3", "store": "PAK'nSAVE Albany", "supermarket": "PaknSave", "suburb": "Albany",
      "name": "Pams Beef Mince 1kg", "brand": "Pams", "size": "1kg",
      "imageUrl": "https://a.fsimg.co.nz/…/5125914.png",
      "price": 11.00, "isOnSpecial": false, "wasPrice": null,
      "unitPrice": 11.00, "unit": "1kg",            // normalised COMPARABLE unit price (server-side); null if N/A
      "priceUpdatedAt": "2026-06-22T08:08:59+00:00", "priceAsOf": "2026-06-24T06:01:44+00:00" },
    { "id": "dddd…00a1", "store": "Woolworths Takapuna", "supermarket": "Woolworths", "suburb": "Takapuna",
      "name": "Woolworths Beef Mince", "price": 12.00, "isOnSpecial": true, "wasPrice": 15.00,
      "unitPrice": 12.00, "unit": "1kg", … },
    { "id": "dddd…00a2", "store": "New World Metro", "supermarket": "NewWorld", "price": 13.50, "isOnSpecial": false, … }
  ]
}]
```
> Root holds only **item metadata** (`itemId` + `description` + `category`); everything else is per-listing inside
> `products[]`. The listing **`name` is a real store name** — use it as the title; render `description` as a
> *grouping caption*, never the title. `products[]` comes back cheapest-first as a neutral default — **re-sort it
> however you like** (the group is ≤7 stores). Drill into one listing via its `id` → `GET /products/{id}`.

### `GET /products/{id}` — the group for one product (cross-store view)

**`{id}` is a product id**; returns the **`ProductGroup` it belongs to** — that listing plus every store listing
sharing its item, so you have the full cross-store picture. An unmatched listing returns a group of one. `404` if
the product id is unknown.

**Response:** a single `ProductGroup` — same shape as an element of `GET /products` above.

> **`supermarket`** = the store group (`Woolworths` | `NewWorld` | `PaknSave`). (Was `chain`.) `wasPrice` is the
> regular price when on special (Woolworths published / Foodstuffs reconstructed, D23).

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

The products under a category node (its **whole subtree**), grouped by item — the **browse alias** of
`GET /products?category={id}`. `id` comes from `GET /categories`.

> **Two equivalent URLs (same `ProductGroup[]`):** `GET /categories/{id}/products` (sub-resource) and
> `GET /products?category={id}` (filter on the products collection). Use whichever fits the call site.

**Query:** `page`, `size`; optional **`?storeId=`** (repeatable — restrict to those stores; see [Filtering by store](#filtering-by-store-storeid)).

**Response:** `ProductGroup[]` — identical shape to [`GET /products`](#get-products--the-product-collection-search--browse).

> **Mixed units caveat:** a broad node (e.g. the *Beef* aisle) mixes per-kg steaks with per-pack sausages, so each
> listing's comparable `unitPrice` spans `1kg` and `1ea` — group by the `unit` field, or query a **shelf**
> (homogeneous units) when you want a clean unit-price comparison.

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

### Admin — match review (D18)

Writes that touch already-ingested data (no crawl/migrate). RESTful — **no verb paths**: a decision is a `PATCH` that
moves a resource's state; a new item is a `POST` to its collection. The route names the resource (no `/admin/`
prefix); access is the **`Admin`** role policy's job (enforcement pending the auth task; open for now).

A reviewer looks at an unmatched/ambiguous listing (`Product`) and resolves it one of three ways:

| Situation | Action |
|---|---|
| A proposed candidate is correct | **approve** it → `PATCH /match-candidates/{id}` `{ "status": "approved" }` |
| No candidate fits, but the listing **is** another existing item | **link** it → `PATCH /products/{id}` `{ "itemId": … }` |
| No candidate fits, and it's genuinely a **new** product | **create** a item (`POST /items`), then **link** it (`PATCH /products/{id}`) |

| Method | Path | Notes |
|---|---|---|
| GET | `/match-candidates` | pending queue, highest-confidence first (`page`,`size`). `MatchCandidateView[]` |
| PATCH | `/match-candidates/{id}` | body `{ "status": "approved" \| "rejected" }`. **approved** links the listing to the candidate's item + clears its sibling candidates; **rejected** stops the matcher proposing this pair again. Returns `MatchCandidateDecision`. `400` bad status, `404` unknown, `409` already decided |
| PATCH | `/products/{id}` | body `{ "itemId": "…" \| null }` — set the listing's item link: an id links it to an **existing** item (clears its pending candidates); `null` **unlinks**. Returns `ProductLinkView`. `404` if the listing or the given item is unknown |
| POST | `/items` | body `{ "name", "description?", "brand?", "size?", "category?" }` — create a **new** item (internal join key; `description` defaults to `name`, `category` to `"Uncategorised"`). Returns **`201 Created`** + `ItemView`. `400` if `name` is blank. **Then link the listing** with `PATCH /products/{id}` using the returned `id` |

`MatchCandidateView`: `{ id, productId, productName, brand, size, supermarket, price, candidateItemId, candidateItem, score, reason }`.
`MatchCandidateDecision`: `{ id, status, itemId }` · `ProductLinkView`: `{ id, itemId }` · `ItemView`: `{ id, name, description, brand, size, category }`.

> The review UI pre-fills the `POST /items` body from the listing it's looking at (its `productName`/
> `brand`/`size` from the queue), then links that listing with the returned `id` — two clean calls instead of one
> magic one. To **link** to a pre-existing item, search the catalogue with `GET /products?q=` and use the result's `id`.

### Category curation (D25) — writes on the `/categories` resource

The item **category** tree is the one curated, owned vocabulary, so it has create/rename/delete (the category is
user-facing; item *products* are an internal join and aren't curated). These are **writes on the same
`/categories` resource** as the public reads — guarded by the **`Admin`** role policy (enforcement pending the auth
task; open for now).

| Method | Path | Notes |
|---|---|---|
| POST | `/categories` | body `{ "kind": "Department\|Aisle\|Shelf", "name", "parentId?" }` — create a category; `slug`/`path` are derived from the name (+ parent). Returns `CategorySummary`. `400` bad kind/name, `404` unknown parent, `409` if the derived `path` already exists |
| PATCH | `/categories/{id}` | body `{ "name" }` — rename the **display name** only (`path`/`slug` stay, so the crawl-time mapper keeps matching it). Returns `CategorySummary`. `404` if unknown |
| DELETE | `/categories/{id}` | **soft-delete**: archives the node + its whole subtree. Returns `{ "archived": n }`. `404` if unknown |

`CategorySummary`: `{ id, kind, name, slug, path, parentId }`.

> **Soft-delete** — archived nodes vanish from `GET /categories` and from category-product queries (an archived id →
> `404`), and the **mapper never un-archives them**, so a deliberately-removed node stays gone across crawls.
> Products under an archived node bubble up to the nearest live ancestor on the next match run.

---

## Front-end flow

| Step | UI shows | Call |
|---|---|---|
| 0 | Store list / picker / map | `GET /stores` |
| 1 | Category navigation | `GET /categories` (`?kind=aisle` for a menu) |
| 2 | Products inside a chosen category | `GET /categories/{id}/products` (or `GET /products?category={id}`) |
| 3 | Click a product → per-store prices | `GET /products/{id}` |
| — | Price chart for a product | `GET /products/{id}/price-history` |
| — | A search box | `GET /products?q=` |
| — | A deals page | `GET /deals` |

The whole flow is now backed end-to-end.

---

## Notes / gotchas for the front-end

- **`category` string vs `categoryId`:** a `ProductGroup`'s `category` is the denormalized leaf **name** (e.g.
  "Chicken Breast, Thighs & Tenders"). The structured tree + ids come from `GET /categories`.
- **Unit price is normalised.** Each listing's `unitPrice` + `unit` (`1kg`/`1L`/`1ea`) is **server-normalised** to
  one comparable base so you can rank by value without re-deriving it (`null` when the store's unit can't be
  parsed). `/deals` still carries the store's raw `unitPrice` + `unitOfMeasure`.
- **Not every Product is matched to an Item.** Unmatched listings still appear — as a group of one (`itemId: null`).
- **Product images (`imageUrl`).** Per-listing, inside `products[]` (and on deals). Can be `null` if a store has
  none. Sources differ: **Woolworths** = its own CDN
  (`assets.woolworths.com.au`, already sized ~200×200); **Foodstuffs** = the `a.fsimg.co.nz` CDN at `400x400` — for
  Foodstuffs you can append **`?w=N`** for a smaller variant (e.g. `?w=200`), and imageless products resolve to a
  generic placeholder image (a real 200 response, just not a photo — same as on the supermarket's own site).
- **Prices are NZD.** `lastSeenAt` is UTC (ISO-8601).
