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
subtree) · `storeId=` (repeatable) · `page` · `size` · `sort`. No filter ⇒ the whole catalogue, paged. `category`
returns only matched listings (the item carries the category); an unknown/archived `category` → `404`.

**Sorting (`sort=`, applied server-side over the whole filtered set *before* paging, so pages are globally correct):**
`unitPriceAsc` (**default** — lowest comparable unit price, falls back to shelf price) · `priceAsc` (lowest shelf
price) · `nameAsc` (group name A–Z) · `discountDesc` (biggest current saving first). Ascending price keys sort null
last; an unknown value falls back to the default. Keys derive from each group's **visible** listings (after the
`storeId` filter). The applied value is echoed in the response `sort`.

**Response:** `PagedResult<ProductGroup>` — a **paged envelope**: `items` is the group array, plus paging fields and
the applied `sort`. `total` = number of **groups** after the `storeId` filter (not raw listings).
```json
{
  "items": [{
  "itemId": "019ef1a2-880e-737b-a6e8-bc376177a9d3", // internal grouping id; null = an unmatched listing
  "description": "Beef Mince 1kg",                  // item caption (D25); client decides usage — see note
  "category": "Beef Mince",                         // item category leaf (denormalized); null if unmatched
  "products": [                                     // THE payload — every store's listing; the client ranks them
    { "id": "dddd…00a3", "sku": "5125914-KGM-000",
      "store": "PAK'nSAVE Albany", "supermarket": "PaknSave", "suburb": "Albany",
      "name": "Pams Beef Mince 1kg", "brand": "Pams", "size": "1kg",
      "imageUrl": "https://a.fsimg.co.nz/…/5125914.png",
      "price": 11.00, "isOnSpecial": false, "wasPrice": null,
      "promoType": null, "memberPrice": null, "multibuyQuantity": null, "multibuyTotal": null,
      "unitPrice": 11.00, "unit": "1kg",            // normalised COMPARABLE unit price (server-side); null if N/A
      "priceUpdatedAt": "2026-06-22T08:08:59+00:00", "priceAsOf": "2026-06-24T06:01:44+00:00" },
    { "id": "dddd…00a1", "store": "Woolworths Takapuna", "supermarket": "Woolworths", "suburb": "Takapuna",
      "name": "Woolworths Beef Mince", "price": 12.00, "isOnSpecial": true, "wasPrice": 15.00,
      "promoType": "Special", "unitPrice": 12.00, "unit": "1kg", … },
    { "id": "dddd…00a2", "store": "New World Metro", "supermarket": "NewWorld", "price": 12.59, "isOnSpecial": false,
      "promoType": "MemberPrice", "memberPrice": 11.29, … }
  ] }],
  "page": 1, "size": 30, "total": 408, "totalPages": 14, "hasMore": true, "sort": "unitPriceAsc"
}
```
> Root holds only **item metadata** (`itemId` + `description` + `category`); everything else is per-listing inside
> `products[]`. `sku` is the supermarket/source product id from the crawler source (not our internal GUID).
> The listing **`name` is a real store name** — use it as the title; render `description` as a
> *grouping caption*, never the title. `products[]` comes back cheapest-first as a neutral default — **re-sort it
> however you like** (the group is ≤7 stores). Drill into one listing via its `id` → `GET /products/{id}`.

> **Promotion fields (2026-07-17, promo-type model):** `promoType` = `"Special"` (public temporary special) |
> `"MemberPrice"` (loyalty-card deal — Woolworths Everyday Rewards / New World Clubcard) | `"Multibuy"` ("N for $X")
> | `null` (no promotion). **`price` is always what a cardless shopper pays**; on a `MemberPrice` listing the card
> price is in `memberPrice` — render it beside the shelf price ("member $11.29"), and note `isOnSpecial` is now
> **`Special` only** (member/multibuy listings have `isOnSpecial: false`). A multibuy carries the
> `multibuyQuantity` + `multibuyTotal` pair (e.g. 3 + 20.00 = "3 for $20") whatever the `promoType`; the pair never
> affects `price`/`unitPrice`.

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
        { "date": "2026-06-22T…", "price": 3.49, "isOnSpecial": true, "wasPrice": null, "promoType": "Special", "memberPrice": null, "unitPrice": … },
        { "date": "2026-06-23T…", "price": 2.99, "isOnSpecial": true, "wasPrice": null, "promoType": "Special", "memberPrice": null, "unitPrice": … },
        { "date": "2026-06-24T…", "price": 3.49, "isOnSpecial": false, "wasPrice": null, "promoType": "MemberPrice", "memberPrice": 2.99, "unitPrice": … }
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

> **Two equivalent URLs (same `PagedResult<ProductGroup>` envelope):** `GET /categories/{id}/products` (sub-resource)
> and `GET /products?category={id}` (filter on the products collection). Use whichever fits the call site.

**Query:** `page`, `size`, `sort` (see [`GET /products`](#get-products--the-product-collection-search--browse) for
the sort values; default `unitPriceAsc`); optional **`?storeId=`** (repeatable — restrict to those stores; see
[Filtering by store](#filtering-by-store-storeid)).

**Response:** `PagedResult<ProductGroup>` — identical envelope + sorting to [`GET /products`](#get-products--the-product-collection-search--browse).

> **Mixed units caveat:** a broad node (e.g. the *Beef* aisle) mixes per-kg steaks with per-pack sausages, so each
> listing's comparable `unitPrice` spans `1kg` and `1ea` — group by the `unit` field, or query a **shelf**
> (homogeneous units) when you want a clean unit-price comparison.

### `GET /deals` — current specials (filterable, paged)

Products on special now, **biggest dollar saving first**. **Filters (all optional, aligned with `GET /products`):**
`supermarket=Woolworths|NewWorld|PaknSave` · `category={id}` (the node's whole subtree — same as `/products?category=`;
an unknown/archived id → `404`) · `storeId=` (repeatable) · `page` · `size`. `supermarket` + `storeId` **intersect**
(a deal must satisfy both). Category filtering only matches **grouped** listings (the item carries the category), same
as `/products`.

**Response:** `PagedResult<DealItem>` — the same paged envelope as `/products` (`items` + `page`/`size`/`total`/
`totalPages`/`hasMore`; `sort` is `null` here — the order is a fixed saving-first default, re-sort client-side):
```json
{
  "items": [{
  "id": "dddd…00a3", "sku": "5125914-KGM-000",
  "product": "woolworths nz beef eye fillet grass fed", "brand": "woolworths nz",
  "imageUrl": "https://assets.woolworths.com.au/images/2010/67807.jpg?...&w=200&h=200",
  "store": "Woolworths Takapuna", "supermarket": "Woolworths",
  "price": 63.99, "wasPrice": 78.99, "saving": 15.00,
  "unitPrice": 63.99, "unitOfMeasure": "1kg",
  "priceUpdatedAt": "2026-06-23T18:08:56+00:00", "priceAsOf": "2026-06-24T06:00:33+00:00"
  }],
  "page": 1, "size": 24, "total": 123, "totalPages": 6, "hasMore": true, "sort": null
}
```
> **A deal = a current PUBLIC special** (`promoType: "Special"`) — narrowed 2026-07-17 by the promo-type model:
> member-only prices (Woolworths club / NW Clubcard) and multibuys are **not** deals; they surface on the product
> listings instead (`promoType`/`memberPrice`/`multibuy*` fields — see `/products`). This is why every item here is
> genuinely buyable at `price` by anyone, card or not.
>
> ⚠️ **`wasPrice`/`saving` may still be `null`.** Woolworths publishes its was-price. NW/PAK publish **none** for a
> public special (the shelf price *is* the promo price), so we *reconstruct* it from our own history — the shelf
> price we last recorded before the product went on special (D23, going-forward only). Until that reconstruction is
> possible (e.g. a Foodstuffs item first seen already on special), the deal is **still returned** but with
> `wasPrice: null` and `saving: null`. Deals with a known saving sort first (biggest first); no-saving promotions
> come after. So the front-end should render `saving`/`wasPrice` **only when present**.
>
> Each deal also carries its listing `id` (drill in via `GET /products/{id}`) and `sku` (the supermarket/source
> product id — tells apart look-alike specials that share brand/name/size).
>
> **Freshness & availability (D28, 2026-07-20):** a deal must have been **seen by a crawl within the last 48h**
> (`lastSeenAt`/`priceAsOf` stays on every item so the UI can still show its age) — a special the crawler stopped
> confirming ages out of `/deals` automatically instead of being served forever. Separately, listings a store has
> **delisted** (missing from 2 consecutive complete crawls) are retired: they disappear from `/deals`, `/products`
> search and the same-product compare (their price history stays queryable via `GET /products/{id}/price-history`).
> No new response fields — retired/stale rows are simply excluded.

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
| Two items are actually the **same** product | **merge** one into the other → `POST /items/{id}/merge` `{ "intoId": … }` |

| Method | Path | Notes |
|---|---|---|
| GET | `/match-candidates` | pending queue, highest-confidence first (`page`,`size`). `MatchCandidateView[]` |
| PATCH | `/match-candidates/{id}` | body `{ "status": "approved" \| "rejected" }`. **approved** links the listing to the candidate's item + clears its sibling candidates; **rejected** stops the matcher proposing this pair again. Returns `MatchCandidateDecision`. `400` bad status, `404` unknown, `409` already decided |
| PATCH | `/products/{id}` | body `{ "itemId": "…" \| null }` — set the listing's item link: an id links it to an **existing** item (clears its pending candidates); `null` **unlinks**. Returns `ProductLinkView`. `404` if the listing or the given item is unknown |
| POST | `/items` | body `{ "name", "description?", "brand?", "size?", "category?" }` — create a **new** item (internal join key; `description` defaults to `name`, `category` to `"Uncategorised"`). Returns **`201 Created`** + `ItemView`. `400` if `name` is blank. **Then link the listing** with `PATCH /products/{id}` using the returned `id` |
| POST | `/items/{id}/merge` | body `{ "intoId": "…" }` — merge item `id` **into** `intoId`: repoints `id`'s products + candidates to the survivor; `id` becomes a redirect so the matcher won't recreate it (non-destructive — store listings untouched). Returns `ItemMergeView`. Idempotent (re-merge into the same survivor = `200`, nothing moved). `400` self-merge / would cycle, `404` unknown item, `409` already merged elsewhere |

`MatchCandidateView`: `{ id, productId, sku, productName, brand, size, supermarket, price, candidateItemId, candidateItem, score, reason }`.
`MatchCandidateDecision`: `{ id, status, itemId }` · `ProductLinkView`: `{ id, itemId }` · `ItemView`: `{ id, name, description, brand, size, category }`.
`ItemMergeView`: `{ sourceId, survivorId, productsMoved, candidatesMoved }`.

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

### Internal — match-coverage report (D30.1)

An **ops/diagnostics** view of how the matcher placed every listing — **not a shopper surface** (items are internal,
D25). Useful for a dashboard tile or a coverage check per supermarket.

| Method | Path | Notes |
|---|---|---|
| GET | `/reports/product-status` | every active-store listing counted per supermarket by match status, as one table + a grand-total row. No params. Returns `ProductStatusReport` |

`ProductStatusReport`: `{ chains: ChainStatusRow[], total: ChainStatusRow }`.
`ChainStatusRow`: `{ supermarket, foodstuffsItem, woolworthsItem, freshChoiceItem, manualItem, pendingReview, held, total }`.

- The first four columns are **linked** listings, grouped by which chain **anchors** the item they joined
  (`foodstuffs:`/`woolworths:`/`freshchoice:`/`manual:` — see [matching.md](internals/matching.md)); the shopper
  never sees the item, this is just where the matcher put the listing.
- `pendingReview` = **待审商品** (unlinked, has a review candidate); `held` = **悬空商品** (unlinked, no candidate —
  guard-held / not yet matchable). Together they're the unmatched listings.
- Every listing is in exactly one column, so each row's columns sum to its `total`, and `total` (the grand row) is the
  column-wise sum of the `chains` rows. `chains` is always the four supermarkets in a fixed order (zero rows included).

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

---

## Decision log

- 2026-07-10 22:39 🧑‍⚖️ Expose `sku` on product listing and match-candidate responses so the review UI can show the supermarket/source SKU instead of our internal GUID.
- 2026-07-11 01:30 🧑‍⚖️ Add `id` + `sku` to `/deals` (`DealItem`) too — deals had neither, so look-alike specials (same brand/name/size) couldn't be told apart or drilled into.
- 2026-07-11 02:00 🧑‍⚖️ Rename `SourceSku` → `Sku` everywhere (domain `Product.Sku`, DB column via `RenameSourceSkuToSku` migration, crawlers, matcher, all DTOs/JSON `sku`, tests, docs). Kevin: the `source` prefix is unwanted; "SKU" already means the store's own id. Full rename, not just the API field.
- 2026-07-11 15:45 🧑‍⚖️ Product list endpoints (`GET /products`, `GET /products?category=`, `GET /categories/{id}/products`) now return a `PagedResult<ProductGroup>` envelope (`items/page/size/total/totalPages/hasMore/sort`) instead of a bare `ProductGroup[]`, with a server-side `sort` (`unitPriceAsc` default · `priceAsc` · `nameAsc` · `discountDesc`) applied over the whole filtered set before paging. `total` = group count after the storeId filter. Front-end (Codex) must switch from reading the array to reading `.items` in lockstep.
- 2026-07-11 21:50 🧑‍⚖️ `/deals` now returns **any current promotion** (`isOnSpecial`), not just ones with a was-price — a deal no longer requires a recoverable regular price (was excluding most Foodstuffs specials until history accumulated). No-was deals come back with `wasPrice`/`saving` `null` and sort after the ones with a known saving. Front-end renders `saving`/`wasPrice` only when present.
- 2026-07-11 22:02 🧑‍⚖️ `/deals` gains `category` + `storeId` filters aligned with `/products` (shared subtree resolver + repository predicate, so they can't drift; `supermarket`+`storeId` intersect; unknown category → 404), and now returns the `PagedResult<DealItem>` envelope (`sort: null`). Front-end reads `.items` (lockstep). Deal ordering is a fixed saving-first default; the client re-sorts.
- 2026-07-17 21:30 🧑‍⚖️ **Promo-type model** (decisions A–E in [internals/promotions-model.md](internals/promotions-model.md)): listings + price-history points gain `promoType` (`"Special"`|`"MemberPrice"`|`"Multibuy"`|null), `memberPrice`, and listings the `multibuyQuantity`/`multibuyTotal` pair. **`price` is now always the cardless shelf price** (Woolworths club deals used to report the member price here). `isOnSpecial` narrowed to `promoType == "Special"` — so **`/deals` = public specials only**; member prices render beside the shelf price on listings, and Clubcard/multibuy promos no longer appear as deals. Front-end (Codex): render `memberPrice`/multibuy badges from the new fields.
- 2026-07-20 15:45 🧑‍⚖️ **Stale-deal fix (D28)** (from the front-end bug report: a Highland Park special from 2026-07-13 was still served by `/deals` after the branch delisted the product): `/deals` now requires the listing to have been **seen by a crawl within 48h**, and listings missing from 2 consecutive complete crawls of their store are **retired** — excluded from `/deals`, `/products` search and the same-product compare (price history stays). No response-shape change; the front-end's price-date staleness hint stays useful but is no longer the only guard.
- 2026-07-23 🧑‍⚖️ **Match-coverage report (D30.1):** new `GET /reports/product-status` — an internal/ops table of every active listing counted per supermarket by match status (aggregated by anchoring chain / 待审 / 悬空) + a grand-total row. Not a shopper surface (items are internal, D25). Added because the front-end asked whether this distribution needed its own endpoint (yes — it can't be derived from the shopper reads). Shape: `ProductStatusReport { chains[], total }`.
