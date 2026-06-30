# Frontend API Requirements

The daily price-finds page should use one aggregated read endpoint. The frontend currently calls:

```http
GET /insights/daily-price-finds?date=2026-06-24
```

In local development, set `VITE_API_BASE_URL=http://localhost:8080` in `src/Zhua.Web/.env.local`.

## Purpose

Return a complete, editor-friendly snapshot for one day of price intelligence:

- latest crawl freshness and active-store coverage
- headline finds worth posting
- cheapest top 3 products across all active stores for major categories
- average comparable unit price by store and category

Use active stores only. Do not include inactive Woolworths Browns Bay/Glenfield rows in current dashboards.

## Query Parameters

| Name | Required | Notes |
|---|---:|---|
| `date` | no | Local NZ date. Defaults to today in `Pacific/Auckland`. |
| `suburb` | no | Future filter for local-radius pages, e.g. `Takapuna`. |
| `storeIds` | no | Optional comma-separated store ids for explicit local comparisons. |

## Response Shape

```ts
type DailyPriceFindsResponse = {
  date: string;
  generatedAt: string;
  crawlWindow: {
    startedAt: string;
    finishedAt: string;
  };
  stats: {
    activeStores: number;
    productsPriced: number;
    matchedProducts: number;
    priceChangesToday: number;
  };
  stores: StoreSummary[];
  headlineFinds: PriceFind[];
  categoryTop3: Record<CategoryKey, CategoryTopProduct[]>;
  categoryAverages: CategoryAverage[];
};

type CategoryKey = "meat" | "eggs" | "milk" | "vegetables";

type StoreSummary = {
  store: string;
  chain: "Woolworths" | "NewWorld" | "PaknSave";
  suburb: string;
  productsScanned: number;
  specials: number;
  lastSeenAt: string;
};

type PriceFind = {
  id: string;
  title: string;
  category: CategoryKey | "frozen" | "deli";
  store: string;
  chain: StoreSummary["chain"];
  price: number;
  previousPrice?: number;
  unitPrice?: number;
  unitOfMeasure?: string;
  changePercent?: number;
  reason: string;
};

type CategoryTopProduct = {
  category: CategoryKey;
  rank: number;
  product: string;
  store: string;
  chain: StoreSummary["chain"];
  price: number;
  unitPrice: number;
  unitOfMeasure: string;
  size?: string;
};

type CategoryAverage = {
  category: CategoryKey;
  store: string;
  chain: StoreSummary["chain"];
  comparableUnit: string;
  averageUnitPrice: number;
  productCount: number;
};
```

## Category Rules

The frontend needs four major categories first:

| Key | Suggested source logic |
|---|---|
| `meat` | Meat, Poultry & Seafood / Meat & Poultry / Fish & Seafood. Prefer rows with comparable `UnitOfMeasure = 1kg`. |
| `eggs` | Egg shelves/categories inside Fridge, Deli & Eggs. Normalize to price per egg where pack size is parseable. |
| `milk` | Fresh milk shelves/categories. Normalize to price per litre. Exclude yoghurt, cream, milk powder, flavoured dessert unless intentionally added later. |
| `vegetables` | Vegetable shelves/categories. Prefer rows with `UnitOfMeasure = 1kg`; keep each/pack rows out of averages unless separately normalized. |

Top 3 should be ranked by normalized unit price, not raw shelf price. If a category has mixed units that cannot be normalized safely, omit those rows from `categoryTop3` and `categoryAverages`.

## Average Rules

`categoryAverages` should calculate one row per active store and category:

- use only rows that match the category and a comparable unit
- use `UnitPrice` where available
- set `comparableUnit` to the normalized unit used by that category, e.g. `1kg`, `1L`, or `1ea`
- include `productCount` so the UI can show whether an average is thin or broad

## Headline Finds Rules

`headlineFinds` should be curated by query logic, not hand-written:

- prioritize material price drops from `PriceSnapshots`
- include practical basket items before novelty items
- avoid cross-town tiny savings unless the item is high-volume or the saving is meaningful
- provide a short `reason` string explaining why this row is worth showing

Good candidates:

- large price drop since previous snapshot
- top normalized price in a major category
- a store/category with unusually high special coverage
- local-radius basket item with a meaningful saving

## Frontend Files

The corresponding TypeScript contracts live in:

```text
src/Zhua.Web/src/types.ts
```
