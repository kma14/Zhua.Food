# zhua.food — crawling logic reference

**The most fragile part of the system.** Every field below is read out of a supermarket's own JSON response.
If a site changes a key, a unit, or how it signals a special, crawling silently degrades (wrong price, missing
special, empty category) **without** throwing. This file is the contract we depend on — when a crawl looks wrong,
start here, then compare against the raw archive (see [§ When a site changes](#when-a-site-changes)).

Code: [`src/Zhua.Crawling/`](../../src/Zhua.Crawling/). The change-only price rule + was-price reconstruction live on
the entity, not the crawler: [`Product.ApplyObservation`](../../src/Zhua.Domain/Entities/Product.cs) (D3/D19/D23).

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

**The observation each product becomes** (`ScrapedProduct` → `ProductObservation`): `Name, Brand, Size,
Gtin, Url, ImageUrl, Price, NonSpecialPrice, PromoType, MemberPrice, MultibuyQuantity, MultibuyTotal, UnitPrice,
UnitOfMeasure` + `CategoryPath` + `Tags`. The price tuple `{Price, PromoType, NonSpecialPrice, UnitPrice,
MemberPrice, MultibuyQuantity, MultibuyTotal}` is what D3 snapshots on change; the entity derives
`IsOnSpecial = (PromoType == Special)`.

### Completeness, gaps & missing-product reconciliation (D28)

Every crawler returns a **`ScrapeResult`** = products + a **gap list**: a human-readable note for every page or
category it had to give up on (after per-page retries). An empty gap list is the crawler's claim that the scrape
covered the store's whole catalog. The orchestrator turns gaps into `CrawlRunStatus.Partial` (gap summary in
`ErrorMessage`) — a run is `Succeeded` only when coverage was complete. **Never silently swallow a failed
page/category in a crawler: report it as a gap.** The distinction exists because of the stale-special bug
(a product PAK'nSAVE Highland Park delisted kept its 2026-07-13 special in /deals for a week):

- **Complete run** → the orchestrator reconciles: every DB product of that store NOT in the scrape gets
  `Product.RecordMissing(now)` — 1st miss starts a streak (`MissingSince`, `ConsecutiveMissingRuns`), the
  **2nd consecutive miss retires the listing** (`IsAvailable=false`, promo fields cleared, `CurrentPrice` kept
  as last-known; deliberately **no synthetic snapshot** — history records only real observations). Being seen
  again resets everything (in `ApplyObservation`).
- **Partial run** → no reconciliation: a lost page must not read as "half the store got delisted".
- Query side: shopper-facing queries exclude `IsAvailable=false`; `/deals` additionally requires
  `LastSeenAt` within a 48h freshness window (`DealQueries.FreshnessWindow`) — covers a dead worker, when
  reconciliation can't run at all.

### ⚠️ Promotions & the "was" price — the highest-value, most fragile logic

`/deals`, the `promoType`/`memberPrice` fields and `wasPrice` in price-history depend entirely on reading each
chain's promo signals right. **The full model + per-chain decode evidence live in
[promotions-model.md](promotions-model.md)** (decided 2026-07-17); this section is the crawler-facing summary.
Universal rule: **`Price` is always the unit price a cardless shopper pays** — never a member price, never a
multibuy total.

| | Woolworths | Foodstuffs (NW / PAK'nSAVE) |
|---|---|---|
| public special → `PromoType.Special` | `price.isSpecial && !price.isClubPrice` | best promo, `cardDependencyFlag: false`, `threshold ≤ 1` |
| member price → `PromoType.MemberPrice` | `price.isClubPrice` (⚠️ **always also flagged `isSpecial`** — split club out FIRST) | best promo, `cardDependencyFlag: true` (93% of NW promos; PAK has none) |
| multibuy pair ("N for $X") | `productTag.multiBuy { quantity, value }` | `threshold > 1`, `rewardValue` = the **total** for N (cents) |
| `Price` (cardless shelf) | club: `originalPrice`; else `salePrice` | `singlePrice.price` (already the promo price for a *public* special) |
| `MemberPrice` | club: `salePrice` | single-unit club deal: `rewardValue/100` (guard `< Price`) |
| regular ("was") price | **published**: `price.originalPrice` | **not published** for public specials → reconstructed (D23) |
| when a promo **ends** | flags → `false`, `salePrice` rises | `promotions[]` **disappears**, `singlePrice.price` rises |

There is **no explicit "special ended" flag on either site** — the end of a special is the *absence* of the
special signal in the next crawl. `ApplyObservation` handles that transition: `PromoType` goes `None`, the
reconstructed was-price is cleared, the product drops out of `/deals`, and the price-rise writes a closing snapshot.
No source publishes promo start/end dates (verified 2026-07-17 — WW has the fields, always null); observed windows
come from our own snapshot history.

**Foodstuffs was-price reconstruction (D23)** — a NW/PAK **public** special's `singlePrice` *is* the promo price
and the regular price is unpublished, so when an observation is `Special` with no source was-price we recover it
from our own prior state:
- product was **off-special last crawl** → the prior shelf price is the "was" (guarded `prior > special`);
- product **still on special** → carry the previously-reconstructed was-price forward;
- **first time we ever saw it, already on special** → unrecoverable, stays `null` (it won't show a saving until it
  re-prices). Going-forward only. Chain-agnostic, but only fires when the source omits the was-price → Woolworths
  is never touched. **Member deals never trigger it** (their `Price` is the undiscounted shelf price — both prices
  are published by the source).

---

## Woolworths

[`src/Zhua.Crawling/Woolworths/WoolworthsCrawler.cs`](../../src/Zhua.Crawling/Woolworths/WoolworthsCrawler.cs) ·
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
| `Sku` | `sku` | |
| `Name` | `name` | |
| `Brand` | `brand` | |
| `Size` | `size.volumeSize` ?? `size.packageType` | |
| `Gtin` | `barcode` | Woolworths **has** a barcode (helps item match, D9) |
| `ImageUrl` | `images.big` ?? `images.small` | |
| `Price` (cardless shelf) | club deal: `price.originalPrice`; else `price.salePrice` ?? `originalPrice` | on a club deal `salePrice` is the MEMBER price |
| `PromoType` | `isClubPrice` → MemberPrice; else `isSpecial` → Special; else `multiBuy` → Multibuy | `isClubPrice ⊂ isSpecial` — club first! |
| `MemberPrice` | club deal: `price.salePrice` (guard `< originalPrice`) | |
| `NonSpecialPrice` (was) | Special only: `price.originalPrice` | **published** was-price; null for club (shelf isn't discounted) |
| `MultibuyQuantity`/`Total` | `productTag.multiBuy.quantity` / `.value` | "3 for $20"; captured whatever the primary type |
| `UnitPrice` | club deal: `size.cupListPrice` ?? `cupPrice`; else `cupPrice` | `cupPrice` tracks `salePrice` → use the list one for club |
| `UnitOfMeasure` | `size.cupMeasure` | e.g. `1kg`, `100g`, `1L`, `1ea` (as published) |
| promo `Tag` | `productTag.tagType` | skip `"Other"` (no real promo); "Low Price" = `IsGreatPrice` (D13) |

**Quirks:** prices are already in **dollars** (not cents). Skip non-`Product` rows (the feed mixes in other types).

---

## Foodstuffs — New World & PAK'nSAVE

[`src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs`](../../src/Zhua.Crawling/Foodstuffs/FoodstuffsCrawler.cs)
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

**Pagination:** response `totalPages`. **⚠️ Truncation (D28):** the search is Algolia-backed and
`paginationLimitedTo` defaults to 1000 — a query matching more products pins `totalHits` at **exactly 1000** and
silently drops everything past page 20 (observed: `Fridge, Deli & Eggs` at every PAK'nSAVE store + NW Browns Bay;
the real count is invisible). `totalHits >= 1000` must be treated as "truncated", never as a real total. The
crawler then **splits the scope by the response's `algoliaSearchResult.facets` values** — `category1NI` first,
`category2NI` if an aisle is still capped (facet counts are computed over the full result set, so they see past
the cap) — and crawls each sub-scope instead of paging the truncated view; the SKU dedup in the orchestrator
absorbs the overlap. A scope that is still capped with no facet left to split by is reported as a gap.
Failed pages retry (3s/8s backoff) before being reported as gaps.

**Field mapping** — `products[]`:

| Our field | Source | Notes |
|---|---|---|
| `Sku` | `productId` | |
| `Name` | `name` | |
| `Brand` | `brand` | |
| `Size` | `displayName` | |
| `Gtin` | — | **none** — search API exposes no barcode → item match falls back to brand+name (D9) |
| `ImageUrl` | **derived** from `productId` | not in the API; built as `https://a.fsimg.co.nz/product/retail/fan/image/400x400/{prefix}.png` where `prefix` = the digits before the first `-`. ⚠️ **prefix, not the full SKU** — `5039995-KGM-000` → `5039995.png`. Same URL the storefront uses; deterministic & free. No-photo products resolve to the CDN placeholder. |
| `Price` (cardless shelf) | `singlePrice.price / 100` | **⚠️ cents** — divide by 100. Public special: this IS the promo price. **Club deal: this is the NON-member price** (the club price is in the promo's `rewardValue`) |
| `PromoType` | from the **best** `promotions[]` element (`bestPromotion: true`, fallback first): `cardDependencyFlag` → MemberPrice; else `threshold > 1` → Multibuy; else Special | decoded 2026-07-17 — see [promotions-model.md](promotions-model.md) |
| `MemberPrice` | single-unit club deal: `rewardValue / 100` (guard `< Price`) | member multibuy (`threshold > 1`): rewardValue is a TOTAL → goes to the pair, `MemberPrice` stays null |
| `MultibuyQuantity`/`Total` | `threshold` / `rewardValue / 100` when `threshold > 1` | "3 for $5.00" |
| `NonSpecialPrice` (was) | **always `null`** at crawl time | **not published** → reconstructed for public specials in `ApplyObservation` (D23) |
| `UnitPrice` | `singlePrice.comparativePrice.pricePerUnit / 100` | cents |
| `UnitOfMeasure` | `comparativePrice.measureDescription` ?? `unitQuantityUom` | |
| `CategoryPath` | `categoryTrees[].level0/1/2` | ExternalId = the **name** (source has no stable category id) |
| promo `Tag` | best promo's `decal` (code) + `rewardType` (label) | decals: `3000` NW public · `4000` NW club · `5000` NW club multibuy · `6000` PAK public |

**Quirks:** prices in **cents** everywhere. No GTIN. The image URL is **not in the response** but is derived from
the SKU prefix (see the `ImageUrl` row). A *public* special only tells you the *current* promo price, never the
original — the whole reason for the D23 reconstruction above; a *club* deal publishes both prices (shelf in
`singlePrice`, club in `rewardValue`) so D23 never fires for it. `rewardType` is always `NEW_PRICE` (2,725/2,725
archived promos) — the real discriminators are `cardDependencyFlag` and `threshold`.

---

## FreshChoice — MyFoodLink (D26)

`src/Zhua.Crawling/FreshChoice/FreshChoiceCrawler.cs`. **A fundamentally different crawler from the other two** —
FreshChoice online runs on the **MyFoodLink** platform, which **server-renders the product data into the page HTML**
(no JSON API to intercept). So this crawler is **plain `HttpClient` + HTML parsing (AngleSharp)** — **no Playwright,
no headed browser** (verified: a plain `curl` returns 200 with the full product HTML; no WAF block). Big win for the
NAS: FreshChoice needs no Chromium.

> FreshChoice is a **Woolworths NZ** banner (not Foodstuffs) but a **separate MyFoodLink storefront** → its own
> `Chain.FreshChoice` + its own crawler. **Independently priced per store** (like Foodstuffs). **Publishes the
> was-price directly** (like Woolworths) → **no D23 reconstruction needed.** Per-store subdomain: the crawler builds
> its base URL from **`Store.ExternalStoreId` = the subdomain** (`hc` → `https://hc.store.freshchoice.co.nz`).

**Status: BUILT + verified 2026-07-20** (first crawl: 1,240 products / 312 specials all with was-price / 10
multibuys / 100% images / 90% unit prices). Parser is golden-file-tested
([`FreshChoiceParserTests`](../../tests/Zhua.Crawling.Tests/FreshChoiceParserTests.cs), fixture = real captured
cards).

**Category pages** (the product source): `GET <base>/category/<slug>?page=<n>` — server-rendered HTML, **~48
products/page**, paginated (`rel="next"` + `?page=N`; a department like `/category/meat` spans its whole subtree,
e.g. 190 results ≈ 4 pages). Follow pages until no `rel="next"`. **M1 departments (hardcoded slugs, like WW):**
`meat · seafood · fruit-vegetables · dairy-eggs · deli` — `deli` currently 404s on the `hc` storefront (linked in
its sidebar but empty); a 404 department is **skipped with a console note, never a crawl failure**. No top-level
frozen department exists (frozen lives inside `groceries` — out of M1 scope for now).

**Product card → our fields** (AngleSharp selectors; one card = `div.talker[id^="line_"]`):
| Field | Selector / rule |
|---|---|
| `Sku` | the card's `id` attr, strip the `line_` prefix (a Mongo ObjectId, e.g. `6a3e00a7f83bb1e8db4ffa5c`) — stable, prefer over the slug |
| `Name` | `span.talker__product-name` (text). **No separate brand field** — the brand is the name's leading word(s), so `Brand = null` (weakens item-match tier 2; future: extract from name) |
| `Size` | `span.talker__name__size` (absent on weight-sold products, e.g. loose fish/meat) |
| `Url` | `a[href^="/lines/"]` → `<base>` + href (the product "line" page) |
| `ImageUrl` | `.talker__section--image img[src]` — CloudFront-signed |
| `Price` (sell) | `strong.price__sell` → strip `$`, parse decimal |
| `PromoType.Special` | card class `talker--Special` **or** a was-price present (`Special`+`Discount` always co-occur on special cards) |
| `PromoType.Multibuy` | card class `talker--Deal` + the "N for $X" pair from `.talker__additional_unit_prices__unit_price--multibuy` ("3 for $20.00 - $83.33 per kg") or the sticker label |
| member price | **none — FreshChoice runs no loyalty program** (sticker census over 5 departments found zero member/everyday variants) |
| `NonSpecialPrice` (was) | `span.talker__prices__was` → "was $5.40" (published directly; guard `was > price`) |
| `UnitPrice` + unit | `span.talker__prices__comparison--UnitPrice` ("$1.60 per 100g" → 1.60 + `100g`); **weight-sold cards have no comparison span** — their `price__units` says `per kg` and the sell price IS the unit price |
| promo tags (D13) | card classes `Special`/`Discount`/`Deal` → `ProductTag`, labelled with the sticker text ("save $1.40" / "3 for $20"). Sticker variants seen: `Special·Discount·Deal·Saving` (+`Tag` = a marketing image, not captured) |

**Category tree (D11):** the category-page sidebar exposes **only top-level departments** (no aisle/shelf links in
the HTML), so **v1 granularity = department-level** `CategoryPath`. The full Department→Aisle→Shelf tree exists as
a CloudFront sidebar JSON (`…cloudfront.net/sidebar/<rootDeptId>/<ver>.json?customer_group[]=…`) with
store-specific params — future work if finer FC categories are needed.

**Gotchas:** prices are dollars (not cents, unlike Foodstuffs). The product data is **only** in the page HTML (there
is NO product XHR — DevTools shows nothing because it's SSR + disk-cached), so `recon` (which dumps JSON) won't catch
it; use `curl <base>/category/<slug>` to see the HTML. HTML parsing is more brittle than JSON — the class names above
(`talker*`, `price__*`) are the contract; if they change, this crawler silently returns fewer/no products. Cards with
class `talker--placeholder` (or no price) are skipped. Raw pages are archived as `.html` (D12).

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

## Decision log

Each entry starts with its timestamp (`YYYY-MM-DD HH:MM`, to the minute), then 🧑‍⚖️ if user-instructed.

- **2026-07-12 21:00** — 🧑‍⚖️ *(Kevin: "1 吧" — build FreshChoice now)* Added the **FreshChoice — MyFoodLink (D26)**
  section as the implementation spec (recon done: SSR HTML, plain HttpClient + AngleSharp, no Playwright). Crawler
  build paused behind the promotions model.
- **2026-07-17 21:35** — 🧑‍⚖️ Promo-type model applied to both crawlers (decisions A–E in
  [promotions-model.md](promotions-model.md)): mappings now produce `PromoType`/`MemberPrice`/`Multibuy*` instead
  of a bare `IsOnSpecial`. Behaviour changes: WW splits `isClubPrice` out of `isSpecial` and reports the
  **non-member** shelf price as `Price` (was the member price); Foodstuffs picks the `bestPromotion` element (was
  `First()`), decodes `cardDependencyFlag`/`threshold`, and no longer flags member/multibuy promos as on-special.
- **2026-07-20 01:32** — 🧑‍⚖️ *(Kevin: build FreshChoice now)* **FreshChoice crawler built & verified** per the
  D26 spec: `FreshChoiceParser` (AngleSharp) + `FreshChoiceCrawler` (plain HttpClient, no Playwright), store seed
  `FreshChoice Hauraki Corner` (`ExternalStoreId` = subdomain `hc`, migration `AddFreshChoiceHaurakiCorner`),
  registered in Worker. Fresh sticker census decoded multibuy (`talker--Deal` → "N for $X") and confirmed **no
  member price on the platform**; weight-sold cards (no size, `per kg` sell units) use the sell price as unit
  price. First crawl: 1,240 products, deli 404-skipped. Known gap: no brand field → FC listings mostly unmatched
  until the matcher learns to take the brand from the name's leading words.

- **2026-07-20 15:40** — 🧑‍⚖️ *(Kevin: implement the stale-deals fix, from the front-end bug report on Highland
  Park's week-old special)* **Completeness + reconciliation (D28) built.** Root cause investigated first via the
  raw archive: Highland Park's 2026-07-19 crawl was complete (all `totalPages` archived) and the SKU was genuinely
  absent — the branch delisted it, but nothing ever retires an unseen product, and /deals had no freshness bound.
  Changes: (1) `IStoreCrawler.FetchAsync` → `ScrapeResult` (products + gaps); all 4 crawlers report gaps + retry
  failed pages instead of silently skipping; a gapped run is `Partial`. (2) The Algolia 1000-hit truncation
  discovered during the investigation is handled by facet-splitting (see Pagination above). (3) Orchestrator
  reconciles missing products after complete runs only: 2 consecutive misses → `IsAvailable=false` + promo
  cleared, **no synthetic snapshot** (Kevin approved threshold 2 + no-snapshot). (4) Shopper queries exclude
  unavailable listings; /deals also requires `LastSeenAt` within 48h (`DealQueries.FreshnessWindow`).

*Keep this file in sync with the crawlers — it documents a contract owned by external sites, so it drifts silently.*
