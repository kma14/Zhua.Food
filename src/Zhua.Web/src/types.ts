export type Supermarket = "Woolworths" | "NewWorld" | "PaknSave";

export type StoreOption = {
  id: string;
  name: string;
  supermarket: Supermarket;
  suburb: string;
  latitude: number;
  longitude: number;
  productCount: number;
  lastCrawledAt: string | null;
};

export type CategoryNode = {
  id: string;
  kind: "Department" | "Aisle" | "Shelf";
  name: string;
  slug: string;
  path: string;
  productCount: number;
  totalProductCount: number;
  children: CategoryNode[];
};

export type ProductListing = {
  id: string;
  store: string;
  supermarket: Supermarket;
  suburb: string;
  name: string;
  brand: string | null;
  size: string | null;
  imageUrl: string | null;
  price: number | null;
  isOnSpecial: boolean;
  wasPrice: number | null;
  unitPrice: number | null;
  unit: string | null;
  priceUpdatedAt: string | null;
  priceAsOf: string;
};

export type ProductGroup = {
  itemId: string | null;
  description: string | null;
  category: string | null;
  products: ProductListing[];
};

export type PriceHistoryPoint = {
  date: string;
  price: number | null;
  isOnSpecial: boolean;
  wasPrice: number | null;
  unitPrice: number | null;
};

export type StorePriceHistory = {
  store: string;
  supermarket: Supermarket;
  suburb: string;
  points: PriceHistoryPoint[];
};

export type ProductPriceHistory = {
  id: string;
  name: string;
  brand: string | null;
  size: string | null;
  stores: StorePriceHistory[];
};

export type DealItem = {
  product: string;
  brand: string | null;
  imageUrl: string | null;
  store: string;
  supermarket: Supermarket;
  price: number | null;
  wasPrice: number | null;
  saving: number | null;
  unitPrice: number | null;
  unitOfMeasure: string | null;
  priceUpdatedAt: string | null;
  priceAsOf: string;
};

export type MatchCandidate = {
  id: string;
  productId: string;
  productName: string;
  brand: string | null;
  size: string | null;
  supermarket: Supermarket;
  price: number | null;
  candidateItemId: string;
  candidateItem: string;
  score: number;
  reason: string | null;
};

export type MatchCandidateDecision = {
  id: string;
  status: "approved" | "rejected";
  itemId: string | null;
};

export type ProductLinkView = {
  id: string;
  itemId: string | null;
};

export type ItemView = {
  id: string;
  name: string;
  description: string | null;
  brand: string | null;
  size: string | null;
  category: string;
};
