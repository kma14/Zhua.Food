import type {
  CategoryNode,
  DealItem,
  ItemView,
  MatchCandidate,
  MatchCandidateDecision,
  PagedResult,
  ProductGroup,
  ProductLinkView,
  ProductPriceHistory,
  ProductSort,
  StoreOption
} from "./types";

const defaultApiBaseUrl = import.meta.env.VITE_API_BASE_URL || "";

type QueryParamValue = string | number | readonly string[] | undefined;
type PagedOrArray<T> = PagedResult<T> | T[];

async function getJson<T>(path: string, params?: Record<string, QueryParamValue>, apiBaseUrl = defaultApiBaseUrl) {
  const url = new URL(path, apiBaseUrl || window.location.origin);
  Object.entries(params ?? {}).forEach(([key, value]) => {
    if (Array.isArray(value)) {
      value.forEach((item) => {
        if (item !== "") url.searchParams.append(key, item);
      });
    } else if (value !== undefined && value !== "") {
      url.searchParams.set(key, String(value));
    }
  });

  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`${path} failed with ${response.status}`);
  }

  return (await response.json()) as T;
}

async function sendJson<T>(path: string, method: "POST" | "PATCH", body: unknown, apiBaseUrl = defaultApiBaseUrl) {
  const url = new URL(path, apiBaseUrl || window.location.origin);
  const response = await fetch(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    throw new Error(`${path} failed with ${response.status}`);
  }

  return (await response.json()) as T;
}

export function getCategories(kind?: "department" | "aisle", storeIds: string[] = [], apiBaseUrl?: string) {
  return getJson<CategoryNode[]>("/categories", { kind, storeId: storeIds }, apiBaseUrl);
}

export function getStores(apiBaseUrl?: string) {
  return getJson<StoreOption[]>("/stores", undefined, apiBaseUrl);
}

export function getCategoryProducts(
  categoryId: string,
  page = 1,
  size = 20,
  sort: ProductSort = "unitPriceAsc",
  storeIds: string[] = [],
  apiBaseUrl?: string
) {
  return getJson<PagedResult<ProductGroup>>(`/categories/${categoryId}/products`, {
    storeId: storeIds,
    page,
    size,
    sort
  }, apiBaseUrl);
}

export function getProductGroup(productId: string, apiBaseUrl?: string) {
  return getJson<ProductGroup>(`/products/${productId}`, undefined, apiBaseUrl);
}

export function getProductPriceHistory(productId: string, days = 14, apiBaseUrl?: string) {
  return getJson<ProductPriceHistory>(`/products/${productId}/price-history`, { days }, apiBaseUrl);
}

export function searchProducts(q: string, size = 8, storeIds: string[] = [], apiBaseUrl?: string, categoryId?: string) {
  return getJson<PagedResult<ProductGroup>>("/products", { q, category: categoryId, storeId: storeIds, page: 1, size }, apiBaseUrl);
}

export async function getDeals(page = 1, size = 8, apiBaseUrl?: string, categoryId?: string, storeIds: string[] = []) {
  const result = await getJson<PagedOrArray<DealItem>>("/deals", { category: categoryId, storeId: storeIds, page, size }, apiBaseUrl);
  if (!Array.isArray(result)) return result;

  return {
    items: result,
    page,
    size,
    total: result.length,
    totalPages: 1,
    hasMore: false,
    sort: null
  } satisfies PagedResult<DealItem>;
}

export function getMatchCandidates(page = 1, size = 100, apiBaseUrl?: string) {
  return getJson<MatchCandidate[]>("/match-candidates", { page, size }, apiBaseUrl);
}

export async function getAllMatchCandidates(apiBaseUrl?: string) {
  const size = 500;
  const maxPages = 50;
  const rows: MatchCandidate[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const pageRows = await getMatchCandidates(page, size, apiBaseUrl);
    rows.push(...pageRows);
    if (pageRows.length < size) break;
  }

  return rows;
}

export function decideMatchCandidate(id: string, status: "approved" | "rejected", apiBaseUrl?: string) {
  return sendJson<MatchCandidateDecision>(`/match-candidates/${id}`, "PATCH", { status }, apiBaseUrl);
}

export function linkProductToItem(productId: string, itemId: string | null, apiBaseUrl?: string) {
  return sendJson<ProductLinkView>(`/products/${productId}`, "PATCH", { itemId }, apiBaseUrl);
}

export function createItem(body: {
  name: string;
  description?: string | null;
  brand?: string | null;
  size?: string | null;
  category?: string | null;
}, apiBaseUrl?: string) {
  return sendJson<ItemView>("/items", "POST", body, apiBaseUrl);
}
