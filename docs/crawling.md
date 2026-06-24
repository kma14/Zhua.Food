# zhua.food — crawling logic reference

**The most fragile part of the system.** Every field below is read out of a supermarket's own JSON response.
If a site changes a key, a unit, or how it signals a special, ingestion silently degrades (wrong price, missing
special, empty category) **without** throwing. This file is the contract we depend on — when a crawl looks wrong,
start here, then compare against the raw archive (see [§ When a site changes](#when-a-site-changes)).

Code: [`src/Zhua.Crawling/`](../src/Zhua.Crawling/). The change-only price rule + was-price reconstruction live on
the entity, not the crawler: [`StoreProduct.ApplyObservation`](../src/Zhua.Domain/Entities/StoreProduct.cs) (D3/D19/D23).

---

## Shared mechanics (both chains)

| Concern | How | Why |
|---|---|---|
| Browser | **Playwright, headed Chromium** (`ZHUA_CRAWL_HEADLESS` to force headless) | headless is **WAF-blocked** (D2/D17); in a container Xvfb gives it a display (D21) |
| What we parse | the page's **intercepted JSON**, never the DOM (D2) | the JSON contract is more stable than rendered HTML |
| Store context | browser **geolocation** = the store's seeded lat/long (D2) | that's how both sites pick "your store" / its prices |
| Fetch transport | `fetch()` **inside the page** (`page.EvaluateAsync`), `credentials:'include'` | reuses the site's own cookies/session/headers — a bare server-side request gets blocked/empty |
| Raw archive | every response saved to `crawl-archive/` (D12), 7-day self-pruning, git-ignored | the DB keeps only mapped fields → the archive is the **only** way to debug/recover source data |
| Politeness | sequential stores, per-request delays (D6) | low ToS risk + avoids tripping rate limits |
| Categories | crawl the store's own tree → many-to-many `StoreCategory` links (D11) | products sit under several shelves; never a denormalized category string |

**The observation each product becomes** (`ScrapedProduct` → `StoreProductObservation`): `Name, Brand, Size,
Gtin, Url, ImageUrl, Price, NonSpecialPrice, IsOnSpecial, UnitPrice, UnitOfMeasure` + `CategoryPath` + `Tags`.
The price tuple `{Price, IsOnSpecial, NonSpecialPrice, UnitPrice}` is what D3 snapshots on change.

### ⚠️ Special detection & the "was" price — the highest-value, most fragile logic

The savings number on `/deals` and `wasPrice` in price-history depend entirely on two things being read right:
**(a) is it on special**, and **(b) what was the regular price**. The two chains differ fundamentally:

| | Woolworths | Foodstuffs (NW / PAK'nSAVE) |
|---|---|---|
| "on special" signal | explicit boolean `price.isSpecial` | **implicit** — a non-empty `promotions[]` array is attached |
| regular ("was") price | **published**: `price.originalPrice` | **not published at all** |
| how we get the "was" | read it directly | **reconstructed** from our own history (D23) — see below |
| when a special **ends** | `isSpecial` → `false`, `salePrice` rises to regular | `promotions[]` **disappears**, `singlePrice.price` rises to regular |

There is **no explicit "special ended" flag on either site** — the end of a special is the *absence* of the
special signal in the next crawl. `ApplyObservation` handles that transition: `IsOnSpecial` goes false, the
reconstructed was-price is cleared, the product drops out of `/deals`, and the price-rise writes a closing snapshot.

**Foodstuffs was-price reconstruction (D23)** — because NW/PAK never publish the regular price, when an
observation is on special with no source was-price we recover it from our own prior state:
- product was **off-special last crawl** → the prior shelf price is the "was" (guarded `prior > special`);
- product **still on special** → carry the previously-reconstructed was-price forward;
- **first time we ever saw it, already on special** → unrecoverable, stays `null` (it won't show a saving until it
  re-prices). Going-forward only. Chain-agnostic, but only fires when the source omits the was-price → Woolworths
  is never touched.

---

## Woolworths

[`src/Zhua.Crawling/Woolworths/WoolworthsCrawler.cs`](../src/Zhua.Crawling/Woolworths/WoolworthsCrawler.cs) ·
`Chain.Woolworths` · national pricing → **1 active store** (Takapuna, D16).

**Endpoint** (browse API, D10):
```
GET https://www.woolworths.co.nz/api/v1/products
      ?dasFilter=Department%3B%3B<slug>%3Bfalse        (repeat per level: Department, Aisle, Shelf)
      &target=browse&inStockProductsOnly=false&size=48&page=<n>
Header: x-requested-with: OnlineShopping.WebApp        (required; plus the page's cookies)
```

**Session:** warm up `https://www.woolworths.co.nz` (cookies). No bearer token.

**Category discovery (the expensive part):** products **don't carry their category**, so we walk the tree to find
them. Departments are a **hardcoded slug list** — `meat-poultry, fruit-veg, fish-seafood, fridge-deli, frozen`;
aisles then shelves are **auto-discovered** from each response's `dasFacets` (`group:"Aisle"`, then `group:"Shelf"`).
An aisle with no shelves is itself the leaf. This is ~**300 requests/store** (~10× Foodstuffs).

**Pagination:** `products.totalItems / 48`.

**⚠️ WAF backoff (do not remove — D17):** the high request volume trips Woolworths' rate limit; a block returns an
**empty body**. `FetchBrowseAsync` retries up to 4× with cooldown **12s / 24s / 36s** + a homepage reload to refresh
the session. Without this, crawls die partway through a department.

**Field mapping** — `products.items[]` where `type == "Product"`:

| Our field | Source | Notes |
|---|---|---|
| `SourceSku` | `sku` | |
| `Name` | `name` | |
| `Brand` | `brand` | |
| `Size` | `size.volumeSize` ?? `size.packageType` | |
| `Gtin` | `barcode` | Woolworths **has** a barcode (helps canonical match, D9) |
| `ImageUrl` | `images.big` ?? `images.small` | |
| `Price` (current) | `price.salePrice` ?? `price.originalPrice` | special price if on special, else shelf |
| `IsOnSpecial` | `price.isSpecial` | explicit boolean |
| `NonSpecialPrice` (was) | `isSpecial ? price.originalPrice : null` | **published** was-price |
| `UnitPrice` | `size.cupPrice` | |
| `UnitOfMeasure` | `size.cupMeasure` | e.g. `1kg`, `100g`, `1L`, `1ea` (as published) |
| promo `Tag` | `productTag.tagType` | skip `"Other"` (no real promo); "Low Price" = `IsGreatPrice` (D13) |

**Quirks:** prices are already in **dollars** (not cents). Skip non-`Product` rows (the feed mixes in other types).

---

## Foodstuffs — New World & PAK'nSAVE

[`src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs`](../src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs)
(shared base, D15) · `NewWorldCrawler` / `PaknSaveCrawler` differ only by **domain** (subclass overrides
`SiteBaseUrl` / `ApiBaseUrl`). Both share the Foodstuffs taxonomy and are **independently priced per branch** (D16).

> ⚠️ This is the **MyFoodLink/api-prod** platform. **FreshChoice is a *different* MyFoodLink storefront** and would
> need its own crawler — these mappings do **not** apply to it.

**Endpoint** (Algolia-backed product search):
```
POST https://api-prod.<banner>.co.nz/v1/edge/search/paginated/products
Header: authorization: Bearer <token>          (anonymous, minted by the SPA — see Session)
Body (mirrors the storefront): filters = stores:<storeId> AND category0NI:"<Department>",
                               hitsPerPage = 50, page = <n>, sortOrder = NI_POPULARITY_ASC
```

**⚠️ Session (two things the SPA does that we must replicate):**
1. **Bearer token** — the edge API needs an anonymous `Authorization: Bearer` token the SPA mints; a raw fetch with
   cookies alone returns empty/401. We **capture it from the page's own `api-prod` requests** during warmup
   (`page.Request` handler), then reuse it.
2. **storeId** — prefer `Store.ExternalStoreId`; else resolve at crawl time from
   `…/next/api/stores/geolocation?lat=&lng=` → `data.id` (returns the **nearest** store, so seed precise coords).
   New World "Takapuna" resolves to the **Shore City** branch.

**Category discovery (cheap):** products **carry their own** `categoryTrees[]` (`level0/level1/level2` =
Department/Aisle/Shelf), so we just query **per department** (hardcoded `level0` names:
`Meat, Poultry & Seafood` · `Fruit & Vegetables` · `Fridge, Deli & Eggs` · `Frozen` — seafood folds under meat) and
read each product's path(s). A product is emitted **once per category tree** whose `level0` is a department we crawl
(the orchestrator dedups by SKU and accumulates the categories). ~**40 requests/store**.

**Pagination:** response `totalPages`.

**Field mapping** — `products[]`:

| Our field | Source | Notes |
|---|---|---|
| `SourceSku` | `productId` | |
| `Name` | `name` | |
| `Brand` | `brand` | |
| `Size` | `displayName` | |
| `Gtin` | — | **none** — search API exposes no barcode → canonical match falls back to brand+name (D9) |
| `ImageUrl` | — | **none** here (derive from fsimg CDN later) |
| `Price` (current) | `singlePrice.price / 100` | **⚠️ cents** — divide by 100. This is the **promo** price while on special |
| `IsOnSpecial` | `promotions` is a non-empty array | **⚠️ implicit** — no boolean |
| `NonSpecialPrice` (was) | **always `null`** at crawl time | **not published** → reconstructed in `ApplyObservation` (D23) |
| `UnitPrice` | `singlePrice.comparativePrice.pricePerUnit / 100` | cents |
| `UnitOfMeasure` | `comparativePrice.measureDescription` ?? `unitQuantityUom` | |
| `CategoryPath` | `categoryTrees[].level0/1/2` | ExternalId = the **name** (source has no stable category id) |
| promo `Tag` | `promotions[0].decal` (code) + `rewardType` (label) | e.g. `rewardType:NEW_PRICE` |

**Quirks:** prices in **cents** everywhere. No GTIN, no image URL. A "special" only tells you the *current* promo
price, never the original — the whole reason for the D23 reconstruction above.

---

## When a site changes

Symptoms → where to look:

| Symptom | Likely cause | Check |
|---|---|---|
| A whole store's crawl returns 0 products | session/auth changed (Foodstuffs bearer or store resolve; Woolworths WAF) | raw archive: is the response empty / 401 / a challenge page? |
| Prices look 100× too big/small | unit changed (cents↔dollars) | compare `singlePrice.price` (FS) / `price.salePrice` (WW) in the archive |
| Everything shows "on special" or nothing does | special signal key changed | WW `price.isSpecial`; FS presence of `promotions[]` |
| Deals show no saving for NW/PAK | expected early (D23 is going-forward) **or** `promotions[]` shape changed | archive: is `promotions` still an array? |
| A category is empty | facet/department slug renamed | WW `dasFacets` group names + the hardcoded dept slugs; FS `category0NI` dept names |
| Categories stopped linking | `categoryTrees` (FS) / `breadcrumb` (WW) shape changed | archive |

**The raw archive is the source of truth for debugging** (D12): every response is on disk under `crawl-archive/`
(`<chain>/<date>/…`). The parsers — `ParseProductsInto` in each crawler — are pure/`internal` and covered by
golden-file tests (`FoodstuffsParserTests`, `WoolworthsParserTests`); when a field moves, fix the parser, drop a
fresh archived response in as a fixture, and assert the new mapping.

**`recon` command** for reverse-engineering a new/changed store API:
`dotnet run --project src/Zhua.Worker -- recon <url>` (headed, dumps every JSON response + request headers/body).

---

*Keep this file in sync with the crawlers — it documents a contract owned by external sites, so it drifts silently.*
