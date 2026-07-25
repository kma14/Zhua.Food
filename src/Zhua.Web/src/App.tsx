import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  createItem,
  decideMatchCandidate,
  getAllMatchCandidates,
  getCategories,
  getCategoryProducts,
  getDeals,
  getProductStatusReport,
  getProducts,
  getProductGroup,
  getProductPriceHistory,
  getStores,
  linkProductToItem,
  searchProducts
} from "./api";
import type {
  CategoryNode,
  DealItem,
  MatchCandidate,
  ChainStatusRow,
  ProductGroup,
  ProductListing,
  PriceHistoryPoint,
  ProductPriceHistory,
  ProductStatusReport,
  ProductSort,
  StoreOption,
  Supermarket
} from "./types";

type Language = "zh" | "en";
type ApiSource = "local" | "nas";
type ProductPromoFilter = "all" | "special" | "member" | "multibuy";
type DealShowcaseMode = "percent" | "saving";

const nasApiBaseUrl = import.meta.env.VITE_NAS_API_BASE_URL || "http://jarvis:8080";
const categoryPageSizeOptions = [10, 20, 30, 50, 100] as const;
const defaultCategoryPageSize = 30;
const dealPageSizeOptions = [10, 20, 30] as const;
const defaultDealPageSize = 10;
const dealShowcaseLimit = 30;
const defaultDealDiscountThreshold = 0.3;
const dealDiscountThresholdOptions = [0.2, 0.3, 0.4, 0.5] as const;
const defaultDealSavingThreshold = 5;
const dealSavingThresholdOptions = [1, 2, 5, 10, 15] as const;
const dealMinSaving = 1;
const dealFreshnessHours = 48;
const productPromoFilters: ProductPromoFilter[] = ["all", "special", "member", "multibuy"];

const copy = {
  zh: {
    loading: "正在加载今日价格...",
    loadingShort: "加载中...",
    failed: "暂时连不上本地价格服务，请确认本地服务已启动后刷新页面。",
    language: "语言",
    heroEyebrow: "奥克兰超市价格雷达",
    heroTitle: "今天去哪买，先看真实价格",
    heroCopy: "按你常去的门店筛选，再看同一类商品里哪些真实门店商品最便宜。点开一行，就能看到同品在各店的实际报价。",
    apiLive: "价格已更新",
    apiSource: "数据源",
    localApi: "本地 API",
    nasApi: "NAS API",
    apiEndpoint: "当前地址",
    apiSourceHint: "本地用于开发调试；NAS 使用家里服务器上的完整后端。",
    searchPlaceholder: "搜 chicken wings / steak / milk...",
    searchButton: "搜索",
    searchTitle: "搜索结果",
    searchEyebrow: "找商品",
    departments: "当前覆盖",
    departmentMetric: "大类",
    aisleMetric: "小类",
    categories: "商品分类",
    allProducts: "全部商品",
    cheapest: "当前分类商品",
    compare: "同品各店报价",
    grouping: "同品说明",
    history: "价格历史",
    historyHint: "过去 30 天趋势；每个点代表一次真实变价，价格会保持到下一次变价。",
    noRecentChange: "暂无更多变价",
    deals: "今日特价",
    dealsEyebrow: "特价",
    dealSource: "按当前门店筛选",
    dealGlobalSource: "值得买 Top 30",
    dealShowcase: "值得买排行",
    dealRankMode: "筛选维度",
    dealModePercent: "按折扣",
    dealModeSaving: "按省钱",
    dealThreshold: "折扣超过",
    dealSavingThreshold: "节省超过",
    dealsDate: "数据日期",
    storeFilter: "门店",
    storeFilterHint: "不选时默认全部门店。分类、排行榜、搜索、详情和特价都会按所选门店展示。",
    storeFilterPending: "当前页面已按所选门店刷新。",
    storeApiMissing: "等待门店 API",
    allStores: "全部门店",
    selectedStores: "已选门店",
    selectedStoresShort: "已选",
    noDealsForStores: "当前门店暂时没有可展示的特价。",
    noStoreData: "当前门店没有这个商品的报价。",
    coverageEyebrow: "分类覆盖",
    noHistory: "这个商品暂时还没有多次变价记录，之后每次抓取会慢慢积累。",
    noResults: "没有找到结果。",
    productCount: "个商品",
    listedStores: "家店有售",
    cheapestStore: "最低门店",
    priceStatus: "价格情况",
    singleStoreStatus: "目前只看到这家在卖",
    samePriceStatus: "所有门店价格一样",
    gapStatusPrefix: "最高和最低相差",
    fresh: "更新于",
    sku: "SKU",
    skuMissing: "SKU 未返回",
    createItemTitle: "创建新 Item",
    createItemHint: "如果右侧不是同一个商品，就新建一个同品组，并把左侧门店商品连进去。",
    createItemName: "Item 名称",
    createItemDescription: "描述",
    createItemBrand: "品牌",
    createItemSize: "规格",
    createItemCategory: "分类",
    createAndLink: "创建并链接",
    categoryRequired: "请选择分类",
    itemNameRequired: "请填写 Item 名称",
    special: "特价",
    unit: "单位价",
    price: "价格",
    brand: "品牌",
    noBrand: "无品牌",
    store: "门店",
    productName: "门店商品名",
    category: "分类",
    was: "原价",
    browseMode: "价格浏览",
    reviewMode: "匹配审核",
    reviewTitle: "人工确认同品匹配",
    reviewIntro: "这里处理还没完全确定的门店商品。确认后，这个真实商品会连到同一个同品组；拒绝后，系统不再推荐这组。",
    woolworthsOnly: "只看 Woolworths",
    allCandidates: "待匹配商品",
    refresh: "刷新",
    approve: "确认匹配",
    reject: "拒绝",
    candidateItem: "候选同品",
    storeProduct: "门店商品",
    matchReason: "匹配原因",
    matchScore: "分数",
    noCandidates: "当前没有需要审核的候选。",
    reviewLoading: "正在加载候选...",
    reviewRulesTitle: "匹配审核规则",
    reviewRuleBrandSize: "先看品牌和规格：brand + size 必须一致或等价。",
    reviewRuleVariant: "低脂、无乳糖、有机、口味、包装数量不同，都不能只因为名字像就合并。",
    reviewRuleDestination: "右侧 Destination Item 里会显示它已经包含的真实门店商品；确认后左侧商品会加入这个组。",
    reviewRuleReject: "拒绝后，这一对 Product 和 Item 不会再被系统推荐。",
    reviewSearchPlaceholder: "搜索待审商品 / SKU / 候选 Item...",
    reviewSearchResult: "筛选结果",
    reviewTargetMissing: "这个商品当前不在待审队列里；可能已经匹配完成，或是悬空商品还没有候选。",
    candidateQueue: "候选队列",
    productQueue: "商品队列",
    candidateItems: "候选 Item",
    candidateCount: "个候选",
    selectedCandidate: "当前候选",
    matchThisProduct: "去匹配",
    sourceProduct: "Source Product",
    destinationItem: "Destination Item",
    currentItemProducts: "当前 Item 里的商品",
    destinationMissing: "暂时没有从现有搜索接口找到这个 Item 里的商品。后端最好补一个 GET /items/{id}/products。",
    detailLoading: "正在加载商品图片和目标 Item...",
    expand: "展开",
    collapse: "收起",
    bestStore: "最低价",
    storeOffers: "门店报价",
    loadMore: "加载更多",
    loadedAll: "已显示全部",
    showingProducts: "已显示",
    sortBy: "排序",
    promoFilter: "优惠",
    promoFilterAll: "全部",
    promoFilterSpecial: "特价",
    promoFilterMember: "会员价",
    promoFilterMultibuy: "多件价",
    pageSize: "每页",
    previousPage: "上一页",
    nextPage: "下一页",
    pageLabel: "第"
  },
  en: {
    loading: "Connecting to the local price API...",
    loadingShort: "Loading...",
    failed: "API is unavailable. Make sure http://localhost:8080 is running, then refresh.",
    language: "Language",
    heroEyebrow: "Auckland grocery price radar",
    heroTitle: "Find where to shop today",
    heroCopy: "Filter to the stores you actually use, then compare real store listings inside each category. Open a row to see same-item prices across stores.",
    apiLive: "API live",
    apiSource: "Data source",
    localApi: "Local API",
    nasApi: "NAS API",
    apiEndpoint: "Endpoint",
    apiSourceHint: "Local is for development. NAS uses the full backend running on the home server.",
    searchPlaceholder: "Search chicken wings / steak / milk...",
    searchButton: "Search",
    searchTitle: "Search results",
    searchEyebrow: "Search",
    departments: "Coverage",
    departmentMetric: "Departments",
    aisleMetric: "Aisles",
    categories: "Categories",
    allProducts: "All products",
    cheapest: "Products in category",
    compare: "Same-item store prices",
    grouping: "Grouping note",
    history: "Price history",
    historyHint: "Past 30-day trend; each point is a real price change and the price holds until the next point.",
    noRecentChange: "No extra changes yet",
    deals: "Today's specials",
    dealsEyebrow: "Deals",
    dealSource: "Filtered by selected stores",
    dealGlobalSource: "Top 30 picks",
    dealShowcase: "Best picks",
    dealRankMode: "Rank by",
    dealModePercent: "Discount",
    dealModeSaving: "Saving",
    dealThreshold: "Discount over",
    dealSavingThreshold: "Saving over",
    dealsDate: "Data date",
    storeFilter: "Stores",
    storeFilterHint: "No selection means all stores. Categories, rankings, search, detail, and specials are shown for selected stores.",
    storeFilterPending: "Data refreshed for selected stores.",
    storeApiMissing: "Waiting for store API",
    allStores: "All stores",
    selectedStores: "Selected stores",
    selectedStoresShort: "Selected",
    noDealsForStores: "No visible specials for the selected stores.",
    noStoreData: "No price row for this item in the selected stores.",
    coverageEyebrow: "Category coverage",
    noHistory: "This item does not have many price-change points yet. History will deepen with twice-daily crawls.",
    noResults: "No results found.",
    productCount: "products",
    listedStores: "stores listing it",
    cheapestStore: "Cheapest store",
    priceStatus: "Price status",
    singleStoreStatus: "Only one store currently lists it",
    samePriceStatus: "All stores have the same price",
    gapStatusPrefix: "Highest and lowest differ by",
    fresh: "updated",
    sku: "SKU",
    skuMissing: "SKU not returned",
    createItemTitle: "Create new Item",
    createItemHint: "If the destination is not the same product, create a new same-item group and link the source listing into it.",
    createItemName: "Item name",
    createItemDescription: "Description",
    createItemBrand: "Brand",
    createItemSize: "Size",
    createItemCategory: "Category",
    createAndLink: "Create and link",
    categoryRequired: "Choose a category",
    itemNameRequired: "Enter an Item name",
    special: "Special",
    unit: "Unit",
    price: "Price",
    brand: "Brand",
    noBrand: "No brand",
    store: "Store",
    productName: "Store product name",
    category: "Category",
    was: "was",
    browseMode: "Price view",
    reviewMode: "Match review",
    reviewTitle: "Manual same-item review",
    reviewIntro: "Resolve uncertain store listings. Approve links the real listing into a same-item group; reject blocks that suggestion.",
    woolworthsOnly: "Woolworths only",
    allCandidates: "Products to match",
    refresh: "Refresh",
    approve: "Approve",
    reject: "Reject",
    candidateItem: "Candidate item",
    storeProduct: "Store product",
    matchReason: "Reason",
    matchScore: "Score",
    noCandidates: "No pending candidates.",
    reviewLoading: "Loading candidates...",
    reviewRulesTitle: "Match review rules",
    reviewRuleBrandSize: "Start with brand and size: brand + size must match or be equivalent.",
    reviewRuleVariant: "Low-fat, lactose-free, organic, flavour, and pack-count variants are not the same item just because names overlap.",
    reviewRuleDestination: "Destination Item shows the real store products already inside it; approving adds the source product to that group.",
    reviewRuleReject: "Rejecting blocks this Product and Item pair from being suggested again.",
    reviewSearchPlaceholder: "Search products / SKU / candidate Item...",
    reviewSearchResult: "Filtered",
    reviewTargetMissing: "This product is not in the pending review queue; it may already be matched, or it is held without candidates.",
    candidateQueue: "Candidate queue",
    productQueue: "Product queue",
    candidateItems: "Candidate Items",
    candidateCount: "candidates",
    selectedCandidate: "Selected candidate",
    matchThisProduct: "Review match",
    sourceProduct: "Source Product",
    destinationItem: "Destination Item",
    currentItemProducts: "Products already in this Item",
    destinationMissing: "Could not find this Item's products through the current search endpoint. A GET /items/{id}/products API would be cleaner.",
    detailLoading: "Loading product images and destination Item...",
    expand: "Expand",
    collapse: "Collapse",
    bestStore: "Best price",
    storeOffers: "Store offers",
    loadMore: "Load more",
    loadedAll: "All products loaded",
    showingProducts: "Showing",
    sortBy: "Sort",
    promoFilter: "Offer",
    promoFilterAll: "All",
    promoFilterSpecial: "Special",
    promoFilterMember: "Member price",
    promoFilterMultibuy: "Multibuy",
    pageSize: "Per page",
    previousPage: "Previous",
    nextPage: "Next",
    pageLabel: "Page"
  }
};

const matchCoverageCopy = {
  zh: {
    title: "匹配覆盖",
    chain: "连锁",
    available: "可用商品",
    linked: "已关联 Item",
    unlinked: "未关联",
    coverage: "关联覆盖率",
    foodstuffsItem: "Foodstuffs 聚合商品",
    woolworthsItem: "Woolworths 聚合商品",
    freshChoiceItem: "FreshChoice 单店聚合",
    manualItem: "手工聚合",
    pendingReview: "待审商品",
    held: "悬空商品",
    total: "合计",
    loading: "正在加载匹配覆盖",
    failed: "匹配覆盖统计加载失败",
    detail: "连锁明细"
  },
  en: {
    title: "Match coverage",
    chain: "Chain",
    available: "Available products",
    linked: "Linked Items",
    unlinked: "Unlinked",
    coverage: "Link coverage",
    foodstuffsItem: "Foodstuffs aggregated",
    woolworthsItem: "Woolworths aggregated",
    freshChoiceItem: "FreshChoice singletons",
    manualItem: "Manually aggregated",
    pendingReview: "Pending review",
    held: "Held",
    total: "Total",
    loading: "Loading match coverage",
    failed: "Could not load match coverage",
    detail: "Chain detail"
  }
};

const chainClass: Record<Supermarket, string> = {
  Woolworths: "chain-woolworths",
  NewWorld: "chain-newworld",
  PaknSave: "chain-paknsave",
  FreshChoice: "chain-freshchoice"
};

const categoryNameZh: Record<string, string> = {
  "Fridge, Deli & Eggs": "冷藏、熟食和鸡蛋",
  Frozen: "冷冻食品",
  "Fruit & Vegetables": "水果和蔬菜",
  "Meat, Poultry & Seafood": "肉类、禽类和海鲜",
  "Butter & Margarine": "黄油和人造黄油",
  Cheese: "奶酪",
  "Chilled Pasta, Pizza & Garlic Bread": "冷藏意面、披萨和蒜蓉面包",
  "Chilled Soups & Ready Meals": "冷藏汤和即食餐",
  "Cream, Custard & Desserts": "奶油、蛋奶冻和甜点",
  "Dairy Free & Meat Free": "无乳和素食替代品",
  "Deli Meats & Smoked Fish": "熟食肉类和烟熏鱼",
  "Dips, Hummus & Antipasti": "蘸酱、鹰嘴豆泥和开胃小食",
  Eggs: "鸡蛋",
  Milk: "牛奶",
  Yoghurt: "酸奶",
  "Frozen Chicken & Meat": "冷冻鸡肉和肉类",
  "Frozen Chips & Hash Browns": "冷冻薯条和薯饼",
  "Frozen Dumplings, Pies & Snacks": "冷冻饺子、派和小食",
  "Frozen Fish & Seafood": "冷冻鱼类和海鲜",
  "Frozen Fruit & Desserts": "冷冻水果和甜点",
  "Frozen Pastry & Bread": "冷冻酥皮和面包",
  "Frozen Pizza & Ready Meals": "冷冻披萨和即食餐",
  "Frozen Vegetables": "冷冻蔬菜",
  "Ice Cream & Sorbet": "冰淇淋和雪葩",
  "Fresh Salad & Herbs": "沙拉菜和香草",
  Fruit: "水果",
  "Organic Fruit & Vegetables": "有机水果和蔬菜",
  Vegetables: "蔬菜",
  Beef: "牛肉",
  "Chicken & Poultry": "鸡肉和禽类",
  "Deli Meats": "熟食肉类",
  Lamb: "羊肉",
  "Mince, Sausages & Meatballs": "肉末、香肠和肉丸",
  "Offal & Bones": "内脏和骨头",
  "Plant Based Alternatives": "植物肉替代品",
  "Pork & Ham": "猪肉和火腿",
  Seafood: "海鲜",
  "Venison & Game": "鹿肉和野味"
};

export function App() {
  const [language, setLanguage] = useState<Language>("zh");
  const [apiSource, setApiSource] = useState<ApiSource>("local");
  const [categories, setCategories] = useState<CategoryNode[]>([]);
  const [selectedDepartmentId, setSelectedDepartmentId] = useState("");
  const [selectedCategoryId, setSelectedCategoryId] = useState("");
  const [groups, setGroups] = useState<ProductGroup[]>([]);
  const [categoryPage, setCategoryPage] = useState(1);
  const [categoryPageSize, setCategoryPageSize] = useState(defaultCategoryPageSize);
  const [categoryTotal, setCategoryTotal] = useState(0);
  const [categoryTotalPages, setCategoryTotalPages] = useState(1);
  const [categoryHasMore, setCategoryHasMore] = useState(false);
  const [categorySort, setCategorySort] = useState<ProductSort>("unitPriceAsc");
  const [categoryPromoFilter, setCategoryPromoFilter] = useState<ProductPromoFilter>("all");
  const [categoryRefreshKey, setCategoryRefreshKey] = useState(0);
  const [selectedProductId, setSelectedProductId] = useState("");
  const [comparison, setComparison] = useState<ProductGroup | null>(null);
  const [history, setHistory] = useState<ProductPriceHistory | null>(null);
  const [deals, setDeals] = useState<DealItem[]>([]);
  const [dealPage, setDealPage] = useState(1);
  const [dealPageSize, setDealPageSize] = useState(defaultDealPageSize);
  const [dealShowcaseMode, setDealShowcaseMode] = useState<DealShowcaseMode>("percent");
  const [dealDiscountThreshold, setDealDiscountThreshold] = useState(defaultDealDiscountThreshold);
  const [dealSavingThreshold, setDealSavingThreshold] = useState(defaultDealSavingThreshold);
  const [expandedDealId, setExpandedDealId] = useState("");
  const [dealTotal, setDealTotal] = useState(0);
  const [dealTotalPages, setDealTotalPages] = useState(1);
  const [dealHasMore, setDealHasMore] = useState(false);
  const [dealRefreshKey, setDealRefreshKey] = useState(0);
  const [stores, setStores] = useState<StoreOption[]>([]);
  const [selectedStoreIds, setSelectedStoreIds] = useState<string[]>([]);
  const [isReviewMode, setIsReviewMode] = useState(false);
  const [matchCandidates, setMatchCandidates] = useState<MatchCandidate[]>([]);
  const [isReviewLoading, setIsReviewLoading] = useState(false);
  const [reviewActionId, setReviewActionId] = useState<string | null>(null);
  const [reviewError, setReviewError] = useState<string | null>(null);
  const [reviewCoverageRefreshKey, setReviewCoverageRefreshKey] = useState(0);
  const [reviewTargetProductId, setReviewTargetProductId] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [searchResults, setSearchResults] = useState<ProductGroup[]>([]);
  const [searchTotal, setSearchTotal] = useState(0);
  const [collapsedSections, setCollapsedSections] = useState({
    deals: false,
    search: false,
    category: false,
    coverage: true
  });
  const [hasSearched, setHasSearched] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isProductsLoading, setIsProductsLoading] = useState(false);
  const [isDealsLoading, setIsDealsLoading] = useState(false);
  const [isProductLoading, setIsProductLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dealsPanelRef = useRef<HTMLDivElement | null>(null);
  const t = copy[language];
  const apiBaseUrl = apiSource === "nas" ? nasApiBaseUrl : "";
  const displayedApiUrl = apiSource === "nas" ? nasApiBaseUrl : `${window.location.origin} → localhost:8080`;

  const flattenedCategories = useMemo(() => flattenCategories(categories), [categories]);
  const selectedCategory = flattenedCategories.find((category) => category.id === selectedCategoryId) ?? null;
  const selectedDepartment = selectedDepartmentId || selectedCategoryId
    ? categories.find((department) => department.id === selectedDepartmentId) ??
      categories.find((department) => department.id === selectedCategoryId || department.children.some((child) => child.id === selectedCategoryId)) ??
      categories[0] ??
      null
    : null;
  const storeOptions = useMemo(() => sortStores(stores), [stores]);
  const selectedStores = useMemo(
    () => storeOptions.filter((store) => selectedStoreIds.includes(store.id)),
    [storeOptions, selectedStoreIds]
  );
  const selectedStoreScope = useMemo(
    () => formatStoreScope(selectedStores, language, t.allStores, t.selectedStoresShort),
    [language, selectedStores, t.allStores, t.selectedStoresShort]
  );
  const staleSelectedDealStores = useMemo(
    () => selectedStores.filter((store) => isStoreStaleForDeals(store, dealFreshnessHours)),
    [selectedStores]
  );
  const dealsEmptyMessage =
    selectedStores.length > 0 && staleSelectedDealStores.length === selectedStores.length
      ? formatStaleDealsMessage(staleSelectedDealStores, language, dealFreshnessHours)
      : t.noDealsForStores;
  const isStoreFilterActive = selectedStoreIds.length > 0;
  const visibleGroups = useMemo(
    () => prepareProductGroups(filterGroupsByPromo(groups, categoryPromoFilter, selectedStores), selectedStores),
    [categoryPromoFilter, groups, selectedStores]
  );
  const detailProducts = useMemo(
    () => filterListingsByStores(comparison?.products ?? [], selectedStores),
    [comparison, selectedStores]
  );
  const selectedListing =
    detailProducts.find((product) => product.id === selectedProductId) ??
    detailProducts[0] ??
    findListingById(visibleGroups, selectedProductId) ??
    null;
  const visibleDeals = useMemo(() => sortDeals(deals, dealShowcaseMode), [dealShowcaseMode, deals]);
  const dealsDataDate = useMemo(() => latestDate(visibleDeals.map((deal) => deal.priceAsOf)), [visibleDeals]);
  const totalProducts = selectedStores.length > 0
    ? selectedStores.reduce((sum, store) => sum + store.productCount, 0)
    : categories.reduce((sum, category) => sum + category.totalProductCount, 0);
  const aisleCount = categories.reduce((sum, category) => sum + category.children.length, 0);
  const loadMatchCandidates = useCallback(async () => {
    setIsReviewLoading(true);
    setReviewError(null);
    try {
      const rows = await getAllMatchCandidates(apiBaseUrl);
      setMatchCandidates(rows);
    } catch (err) {
      setReviewError(err instanceof Error ? err.message : t.failed);
    } finally {
      setIsReviewLoading(false);
    }
  }, [apiBaseUrl, t.failed]);
  const refreshReviewData = useCallback(async () => {
    await loadMatchCandidates();
    setReviewCoverageRefreshKey((current) => current + 1);
  }, [loadMatchCandidates]);

  function openReviewForProduct(productId: string) {
    setReviewTargetProductId(productId);
    setIsReviewMode(true);
  }

  function toggleStoreFilter(id: string) {
    setCategoryPage(1);
    setDealPage(1);
    setExpandedDealId("");
    setSelectedStoreIds((current) => {
      if (current.includes(id)) return current.filter((value) => value !== id);
      const next = [...current, id];
      return next.length === storeOptions.length ? [] : next;
    });
  }

  function toggleSection(section: keyof typeof collapsedSections) {
    setCollapsedSections((current) => ({ ...current, [section]: !current[section] }));
  }

  function changeApiSource(nextSource: ApiSource) {
    if (nextSource === apiSource) return;
    setApiSource(nextSource);
    setSelectedStoreIds([]);
    setCategoryPage(1);
    setDealPage(1);
    setSelectedProductId("");
    setSearchResults([]);
    setSearchTotal(0);
    setHasSearched(false);
    setComparison(null);
    setHistory(null);
    setError(null);
    setIsLoading(true);
  }

  async function handleCandidateAction(id: string, status: "approved" | "rejected") {
    setReviewActionId(id);
    setReviewError(null);
    try {
      await decideMatchCandidate(id, status, apiBaseUrl);
      await refreshReviewData();
    } catch (err) {
      setReviewError(err instanceof Error ? err.message : t.failed);
    } finally {
      setReviewActionId(null);
    }
  }

  useEffect(() => {
    let cancelled = false;

    async function loadInitialData() {
      try {
        const [categoryTree, storeRows] = await Promise.all([
          getCategories(undefined, selectedStoreIds, apiBaseUrl),
          getStores(apiBaseUrl).catch(() => [] as StoreOption[])
        ]);
        if (cancelled) return;

        setCategories(categoryTree);
        setStores(storeRows);

        const flat = flattenCategories(categoryTree);
        const hasCategorizedProducts = categoryTree.some((category) => category.totalProductCount > 0);
        if (selectedStoreIds.length > 0 && !hasCategorizedProducts) {
          setSelectedDepartmentId("");
          setSelectedCategoryId("");
          return;
        }
        const defaultCategory =
          flat.find((category) => category.path === "meat-poultry-seafood/chicken-poultry") ??
          flat.find((category) => category.kind === "Aisle") ??
          flat[0];
        const defaultDepartment =
          categoryTree.find((department) => department.id === defaultCategory?.id || department.children.some((child) => child.id === defaultCategory?.id)) ??
          categoryTree[0];
        setSelectedDepartmentId((current) =>
          categoryTree.some((department) => department.id === current) ? current : defaultDepartment?.id || ""
        );
        setSelectedCategoryId((current) =>
          flat.some((category) => category.id === current) ? current : defaultCategory?.id || ""
        );
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : t.failed);
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    }

    loadInitialData();

    return () => {
      cancelled = true;
    };
  }, [apiBaseUrl, selectedStoreIds, t.failed]);

  useEffect(() => {
    let cancelled = false;

    async function loadDeals() {
      setIsDealsLoading(true);
      try {
        const allDeals = await getAllDealsForRanking(apiBaseUrl, selectedStoreIds);
        if (cancelled) return;
        const rankedDeals = sortDeals(allDeals, dealShowcaseMode)
          .filter((deal) => isShowcaseDeal(deal, dealShowcaseMode, dealDiscountThreshold, dealSavingThreshold))
          .slice(0, dealShowcaseLimit);
        const totalPages = Math.max(1, Math.ceil(rankedDeals.length / dealPageSize));
        const page = Math.min(dealPage, totalPages);
        const pageItems = rankedDeals.slice((page - 1) * dealPageSize, page * dealPageSize);
        setDeals(pageItems);
        setDealPage(page);
        setDealTotal(rankedDeals.length);
        setDealTotalPages(totalPages);
        setDealHasMore(page < totalPages);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : t.failed);
      } finally {
        if (!cancelled) setIsDealsLoading(false);
      }
    }

    loadDeals();

    return () => {
      cancelled = true;
    };
  }, [
    apiBaseUrl,
    dealDiscountThreshold,
    dealPage,
    dealPageSize,
    dealRefreshKey,
    dealSavingThreshold,
    dealShowcaseMode,
    selectedStoreIds,
    t.failed
  ]);

  useEffect(() => {
    if (!expandedDealId) return;

    function handlePointerDown(event: MouseEvent | TouchEvent) {
      const target = event.target;
      if (target instanceof Node && dealsPanelRef.current?.contains(target)) return;
      setExpandedDealId("");
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setExpandedDealId("");
    }

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("touchstart", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("touchstart", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [expandedDealId]);

  useEffect(() => {
    let cancelled = false;

    async function loadProducts() {
      setIsProductsLoading(true);
      try {
        if (categoryPromoFilter !== "all") {
          const allGroups = await getAllProductGroupsForFilter(
            selectedCategoryId || undefined,
            categorySort,
            selectedStoreIds,
            apiBaseUrl
          );
          if (cancelled) return;

          const filtered = filterGroupsByPromo(allGroups, categoryPromoFilter, selectedStores);
          const total = filtered.length;
          const totalPages = Math.max(1, Math.ceil(total / categoryPageSize));
          const page = Math.min(categoryPage, totalPages);
          const pageItems = filtered.slice((page - 1) * categoryPageSize, page * categoryPageSize);

          setGroups(pageItems);
          setCategoryPage(page);
          setCategoryTotal(total);
          setCategoryTotalPages(totalPages);
          setCategoryHasMore(page < totalPages);
          setSelectedProductId(bestListing(pageItems[0], selectedStores)?.id ?? "");
          return;
        }

        const result = selectedCategoryId
          ? await getCategoryProducts(selectedCategoryId, categoryPage, categoryPageSize, categorySort, selectedStoreIds, apiBaseUrl)
          : await getProducts(categoryPage, categoryPageSize, categorySort, selectedStoreIds, apiBaseUrl);
        if (cancelled) return;
        const prepared = prepareProductGroups(result.items, selectedStores);
        setGroups(result.items);
        setCategoryPage(result.page);
        setCategoryTotal(result.total);
        setCategoryTotalPages(Math.max(1, result.totalPages));
        setCategoryHasMore(result.hasMore);
        setSelectedProductId(bestListing(prepared[0], selectedStores)?.id ?? "");
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : t.failed);
      } finally {
        if (!cancelled) setIsProductsLoading(false);
      }
    }

    loadProducts();

    return () => {
      cancelled = true;
    };
  }, [
    apiBaseUrl,
    categoryPage,
    categoryPageSize,
    categoryPromoFilter,
    categoryRefreshKey,
    categorySort,
    selectedCategoryId,
    selectedStoreIds,
    selectedStores,
    t.failed
  ]);

  function changeCategoryPage(nextPage: number) {
    if (isProductsLoading) return;
    const boundedPage = Math.min(Math.max(1, nextPage), categoryTotalPages);
    if (boundedPage !== categoryPage) setCategoryPage(boundedPage);
  }

  function changeCategorySort(nextSort: ProductSort) {
    setCategorySort(nextSort);
    setCategoryPage(1);
  }

  function changeCategoryPromoFilter(nextFilter: ProductPromoFilter) {
    setCategoryPromoFilter(nextFilter);
    setCategoryPage(1);
  }

  function changeCategoryPageSize(nextSize: number) {
    setCategoryPageSize(nextSize);
    setCategoryPage(1);
  }

  function refreshCategoryProducts() {
    setCategoryRefreshKey((current) => current + 1);
  }

  function changeDealPage(nextPage: number) {
    if (isDealsLoading) return;
    const boundedPage = Math.min(Math.max(1, nextPage), dealTotalPages);
    setExpandedDealId("");
    if (boundedPage !== dealPage) setDealPage(boundedPage);
  }

  function changeDealPageSize(nextSize: number) {
    setExpandedDealId("");
    setDealPageSize(nextSize);
    setDealPage(1);
  }

  function changeDealShowcaseMode(nextMode: DealShowcaseMode) {
    setExpandedDealId("");
    setDealShowcaseMode(nextMode);
    setDealPage(1);
  }

  function changeDealDiscountThreshold(nextThreshold: number) {
    setExpandedDealId("");
    setDealDiscountThreshold(nextThreshold);
    setDealPage(1);
  }

  function changeDealSavingThreshold(nextThreshold: number) {
    setExpandedDealId("");
    setDealSavingThreshold(nextThreshold);
    setDealPage(1);
  }

  function refreshDeals() {
    setExpandedDealId("");
    setDealRefreshKey((current) => current + 1);
  }

  useEffect(() => {
    setHasSearched(false);
    setSearchResults([]);
    setSearchTotal(0);
  }, [selectedCategoryId, selectedStoreIds]);

  useEffect(() => {
    if (isReviewMode) void loadMatchCandidates();
  }, [isReviewMode, loadMatchCandidates]);

  useEffect(() => {
    if (!selectedProductId) {
      setComparison(null);
      setHistory(null);
      return;
    }

    let cancelled = false;

    async function loadProduct() {
      setIsProductLoading(true);
      try {
        const [productGroup, productHistory] = await Promise.all([
          getProductGroup(selectedProductId, apiBaseUrl),
          getProductPriceHistory(selectedProductId, 30, apiBaseUrl)
        ]);
        if (cancelled) return;
        setComparison(productGroup);
        setHistory(productHistory);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : t.failed);
      } finally {
        if (!cancelled) setIsProductLoading(false);
      }
    }

    loadProduct();

    return () => {
      cancelled = true;
    };
  }, [apiBaseUrl, selectedProductId, t.failed]);

  async function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const query = searchTerm.trim();
    if (!query) return;

    setHasSearched(true);
    const result = await searchProducts(query, 24, selectedStoreIds, apiBaseUrl);
    setSearchResults(result.items);
    setSearchTotal(result.total);
    setCollapsedSections((current) => ({ ...current, search: false }));
  }

  if (isLoading) {
    return (
      <main className="loading-shell">
        <div className="loading-card">{t.loading}</div>
      </main>
    );
  }

  if (error && categories.length === 0) {
    return (
      <main className="loading-shell">
        <div className="loading-card error-card">{t.failed}</div>
      </main>
    );
  }

  return (
    <main className="app-shell">
      <div className="utility-bar">
        <span className="api-pill">{t.apiLive}</span>
        <div className="language-toggle" role="group" aria-label={t.reviewMode}>
          <button
            className={!isReviewMode ? "active" : ""}
            onClick={() => {
              setReviewTargetProductId(null);
              setIsReviewMode(false);
            }}
          >
            {t.browseMode}
          </button>
          <button className={isReviewMode ? "active" : ""} onClick={() => setIsReviewMode(true)}>
            {t.reviewMode}
          </button>
        </div>
        <span>{t.language}</span>
        <div className="language-toggle" role="group" aria-label={t.language}>
          <button className={language === "zh" ? "active" : ""} onClick={() => setLanguage("zh")}>
            中文
          </button>
          <button className={language === "en" ? "active" : ""} onClick={() => setLanguage("en")}>
            EN
          </button>
        </div>
      </div>

      <section className="hero">
        <div>
          <p className="eyebrow">{t.heroEyebrow}</p>
          <h1>{t.heroTitle}</h1>
          <p className="hero-copy">{t.heroCopy}</p>
        </div>
        <form className="search-box" onSubmit={handleSearch}>
          <input
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder={t.searchPlaceholder}
            aria-label={t.searchPlaceholder}
          />
          <button>{t.searchButton}</button>
        </form>
      </section>

      {isReviewMode ? (
        <ReviewWorkspaceV2
          candidates={matchCandidates}
          categories={flattenedCategories}
          isLoading={isReviewLoading}
          actionId={reviewActionId}
          error={reviewError}
          language={language}
          apiBaseUrl={apiBaseUrl}
          coverageRefreshKey={reviewCoverageRefreshKey}
          targetProductId={reviewTargetProductId}
          t={t}
          onRefresh={refreshReviewData}
          onTargetConsumed={() => setReviewTargetProductId(null)}
          onAction={handleCandidateAction}
        />
      ) : (
        <>
          <section className="control-panel" aria-label={t.storeFilter}>
            <article className="api-source-card">
              <div>
                <p className="eyebrow">{t.apiSource}</p>
                <strong>{apiSource === "nas" ? t.nasApi : t.localApi}</strong>
                <small>{t.apiSourceHint}</small>
              </div>
              <div className="source-toggle" role="group" aria-label={t.apiSource}>
                <button className={apiSource === "local" ? "active" : ""} onClick={() => changeApiSource("local")}>
                  {t.localApi}
                </button>
                <button className={apiSource === "nas" ? "active" : ""} onClick={() => changeApiSource("nas")}>
                  {t.nasApi}
                </button>
              </div>
              <small className="api-endpoint">
                {t.apiEndpoint}: {displayedApiUrl}
              </small>
            </article>

            <article className="store-filter-card">
              <div className="store-filter-heading">
                <div>
                  <p className="eyebrow">{t.storeFilter}</p>
                  <strong>{isStoreFilterActive ? `${t.selectedStores} ${selectedStoreIds.length}` : t.allStores}</strong>
                  <small>{isStoreFilterActive ? t.storeFilterPending : t.storeFilterHint}</small>
                </div>
                <button
                  className={!isStoreFilterActive ? "active" : ""}
                  onClick={() => {
                    setCategoryPage(1);
                    setDealPage(1);
                    setSelectedStoreIds([]);
                  }}
                >
                  {t.allStores}
                </button>
              </div>
              <div className="store-filter-actions">
                {storeOptions.length === 0 && <span className="store-api-missing">{t.storeApiMissing}</span>}
                {storeOptions.map((option) => (
                  <button
                    key={option.id}
                    className={selectedStoreIds.includes(option.id) ? "active" : ""}
                    onClick={() => toggleStoreFilter(option.id)}
                  >
                    <ChainBadge chain={option.supermarket} />
                    <span className="store-filter-label">
                      <strong>{formatStoreBranch(option.name, option.supermarket, language)}</strong>
                      <small>
                        {option.productCount.toLocaleString()} {t.productCount}
                        {option.lastCrawledAt ? ` · ${formatShortDateTime(option.lastCrawledAt)}` : ""}
                      </small>
                    </span>
                  </button>
                ))}
              </div>
            </article>
          </section>

          <section className="stats-grid" aria-label={t.departments}>
            <Metric label={t.productCount} value={totalProducts.toLocaleString()} />
            <Metric label={t.departmentMetric} value={categories.length.toLocaleString()} />
            <Metric label={t.aisleMetric} value={aisleCount.toLocaleString()} />
            <Metric label={t.deals} value={dealTotal.toLocaleString()} />
          </section>

          <CollapsibleSection
            className="deals-panel"
            eyebrow={t.dealsEyebrow}
            title={t.deals}
            meta={dealsDataDate ? `${t.dealsDate} ${formatDate(dealsDataDate)}` : undefined}
            pill={`${dealTotal.toLocaleString()} · ${t.dealGlobalSource} · ${selectedStoreScope}`}
            collapsed={collapsedSections.deals}
            expandLabel={t.expand}
            collapseLabel={t.collapse}
            onToggle={() => toggleSection("deals")}
          >
            <div className="table-heading deals-heading">
              <div>
                <h3>{t.dealShowcase}</h3>
                <span>
                  {t.pageLabel} {dealPage} / {dealTotalPages} · {t.showingProducts} {visibleDeals.length.toLocaleString()} / {dealTotal.toLocaleString()} · {selectedStoreScope}
                </span>
              </div>
              <div className="table-controls">
                <button className="refresh-button" onClick={refreshDeals} disabled={isDealsLoading}>
                  {isDealsLoading ? t.loadingShort : t.refresh}
                </button>
                <div className="deal-mode-control" aria-label={t.dealRankMode}>
                  <span>{t.dealRankMode}</span>
                  <div className="segmented-control">
                    <button
                      className={dealShowcaseMode === "percent" ? "active" : ""}
                      onClick={() => changeDealShowcaseMode("percent")}
                      type="button"
                    >
                      {t.dealModePercent}
                    </button>
                    <button
                      className={dealShowcaseMode === "saving" ? "active" : ""}
                      onClick={() => changeDealShowcaseMode("saving")}
                      type="button"
                    >
                      {t.dealModeSaving}
                    </button>
                  </div>
                </div>
                <label className="sort-control">
                  <span>{dealShowcaseMode === "percent" ? t.dealThreshold : t.dealSavingThreshold}</span>
                  {dealShowcaseMode === "percent" ? (
                    <select
                      value={dealDiscountThreshold}
                      onChange={(event) => changeDealDiscountThreshold(Number(event.target.value))}
                    >
                      {dealDiscountThresholdOptions.map((threshold) => (
                        <option key={threshold} value={threshold}>
                          {formatPercentThreshold(threshold)}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <select
                      value={dealSavingThreshold}
                      onChange={(event) => changeDealSavingThreshold(Number(event.target.value))}
                    >
                      {dealSavingThresholdOptions.map((threshold) => (
                        <option key={threshold} value={threshold}>
                          {formatSavingThreshold(threshold)}
                        </option>
                      ))}
                    </select>
                  )}
                </label>
                <label className="sort-control">
                  <span>{t.pageSize}</span>
                  <select value={dealPageSize} onChange={(event) => changeDealPageSize(Number(event.target.value))}>
                    {dealPageSizeOptions.map((size) => (
                      <option key={size} value={size}>
                        {size}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            </div>
            <div className="deals-grid" ref={dealsPanelRef}>
              {visibleDeals.length === 0 && <p className="empty-state">{dealsEmptyMessage}</p>}
              {visibleDeals.map((deal) => {
                const isExpanded = expandedDealId === deal.id;
                const canUseCurrentDetail = isExpanded && selectedProductId === deal.id && comparison !== null;
                const expandedListings = canUseCurrentDetail ? detailProducts : [];
                const expandedSelectedListing =
                  expandedListings.find((listing) => listing.id === deal.id) ?? expandedListings[0] ?? null;
                const expandedWeightNotice = formatWeightSoldNotice(expandedSelectedListing, language);
                return (
                  <article key={deal.id} className={`deal-card${isExpanded ? " expanded" : ""}`}>
                    <button
                      type="button"
                      className="deal-card-button"
                      onClick={() => {
                        setExpandedDealId((current) => (current === deal.id ? "" : deal.id));
                        setSelectedProductId(deal.id);
                      }}
                    >
                      <div className="deal-card-top">
                        <ProductImage imageUrl={imageForDeal(deal)} name={deal.product} size="medium" />
                        <div>
                          <ChainBadge chain={deal.supermarket} />
                          <h3>{deal.product}</h3>
                          {deal.brand && <p>{deal.brand}</p>}
                        </div>
                      </div>
                      <div className="deal-price">
                        <strong>{formatDealPrimaryPrice(deal)}</strong>
                        {formatDealSaving(deal, language) && <em className="deal-saving">{formatDealSaving(deal, language)}</em>}
                        {deal.wasPrice !== null && (
                          <span>
                            {t.was} {formatDealWasPrice(deal)}
                          </span>
                        )}
                      </div>
                      <footer>
                        <span>{formatStoreBranch(deal.store, deal.supermarket, language)}</span>
                        {deal.unitPrice !== null && deal.unitOfMeasure && <span>{formatUnit(deal.unitPrice, deal.unitOfMeasure)}</span>}
                      </footer>
                      <span className={`deal-date${isStalePrice(deal.priceAsOf) ? " stale" : ""}`} title={formatDealDateTitle(deal, language)}>
                        {formatDealDate(deal.priceAsOf, language)}
                      </span>
                    </button>
                    {isExpanded && (
                      <div className="deal-expanded">
                        {isProductLoading && selectedProductId === deal.id && <p className="empty-state">{t.loadingShort}</p>}
                        {!isProductLoading && canUseCurrentDetail && (
                          <>
                            <div className="deal-expanded-summary">
                              <span>{t.cheapestStore}</span>
                              <StoreIdentity
                                store={expandedListings[0]?.store ?? deal.store}
                                chain={expandedListings[0]?.supermarket ?? deal.supermarket}
                                language={language}
                              />
                              <span>{t.priceStatus}</span>
                              <strong>{describeDealPriceStatus(deal, expandedListings, t, language)}</strong>
                              {expandedWeightNotice && <p>{expandedWeightNotice}</p>}
                            </div>
                            <div className="deal-expanded-offers">
                              <strong>{t.storeOffers}</strong>
                              {expandedListings.slice(0, 2).map((listing) => (
                                <article key={listing.id} className="price-row compact">
                                  <div>
                                    <StoreIdentity store={listing.store} chain={listing.supermarket} language={language} />
                                  </div>
                                  <div>
                                    <strong>{formatMaybeCurrency(listing.price)}</strong>
                                    <PromotionBadges listing={listing} language={language} compact />
                                    {listing.unitPrice !== null && listing.unit && <span>{formatUnit(listing.unitPrice, listing.unit)}</span>}
                                  </div>
                                  <small>
                                    <span>{listing.name}</span>
                                    <span>{formatListingMeta(listing, comparison, language)}</span>
                                  </small>
                                </article>
                              ))}
                              {expandedListings.length > 2 && (
                                <p className="deal-expanded-more">
                                  {language === "zh" ? `还有 ${expandedListings.length - 2} 家报价在下方详情区` : `${expandedListings.length - 2} more offers in the detail section below`}
                                </p>
                              )}
                            </div>
                          </>
                        )}
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
            <div className="pagination-bar">
              <button onClick={() => changeDealPage(dealPage - 1)} disabled={isDealsLoading || dealPage <= 1}>
                {t.previousPage}
              </button>
              <div className="page-number-group" aria-label={t.pageLabel}>
                {paginationPages(dealPage, dealTotalPages).map((page, index) =>
                  page === "ellipsis" ? (
                    <span key={`deal-ellipsis-${index}`} className="page-ellipsis">…</span>
                  ) : (
                    <button
                      key={page}
                      className={page === dealPage ? "active" : ""}
                      onClick={() => changeDealPage(page)}
                      disabled={isDealsLoading || page === dealPage}
                    >
                      {page}
                    </button>
                  )
                )}
              </div>
              <button onClick={() => changeDealPage(dealPage + 1)} disabled={isDealsLoading || !dealHasMore}>
                {t.nextPage}
              </button>
            </div>
          </CollapsibleSection>

          {hasSearched && (
            <CollapsibleSection
              className="search-panel"
              eyebrow={t.searchEyebrow}
              title={t.searchTitle}
              pill={`${searchTotal.toLocaleString()} ${t.productCount} · ${selectedStoreScope}`}
              collapsed={collapsedSections.search}
              expandLabel={t.expand}
              collapseLabel={t.collapse}
              onToggle={() => toggleSection("search")}
            >
              {searchResults.length === 0 ? (
                <p className="empty-state">{t.noResults}</p>
              ) : (
                <div className="search-results">
                  {rankProductGroups(searchResults, selectedStores).map((group) => {
                    const listing = bestListing(group, selectedStores);
                    if (!listing) return null;
                    return (
                      <SearchResultCard
                        key={listing.id}
                        group={group}
                        listing={listing}
                        listings={filterListingsByStores(group.products, selectedStores)}
                        language={language}
                        t={t}
                        onSelect={() => {
                          setSelectedProductId(listing.id);
                          setCollapsedSections((current) => ({ ...current, category: false }));
                        }}
                      />
                    );
                  })}
                </div>
              )}
            </CollapsibleSection>
          )}

          <CollapsibleSection
            className="category-panel"
            eyebrow={t.categories}
            title={selectedCategory ? translateCategoryName(selectedCategory.name, language) : t.allProducts}
            pill={`${categoryTotal.toLocaleString()} ${t.productCount} · ${selectedStoreScope}`}
            collapsed={collapsedSections.category}
            expandLabel={t.expand}
            collapseLabel={t.collapse}
            onToggle={() => toggleSection("category")}
          >

            <div className="department-tabs" aria-label={t.departmentMetric}>
              <button
                className={!selectedCategoryId ? "active" : ""}
                onClick={() => {
                  setCategoryPage(1);
                  setDealPage(1);
                  setSelectedDepartmentId("");
                  setSelectedCategoryId("");
                }}
              >
                <strong>{t.allProducts}</strong>
                <span>{totalProducts}</span>
              </button>
              {categories.map((department) => (
                <button
                  key={department.id}
                  className={selectedDepartment?.id === department.id ? "active" : ""}
                  onClick={() => {
                    setCategoryPage(1);
                    setDealPage(1);
                    setSelectedDepartmentId(department.id);
                    setSelectedCategoryId(department.children[0]?.id ?? department.id);
                  }}
                >
                  <strong>{translateCategoryName(department.name, language)}</strong>
                  <span>{department.totalProductCount}</span>
                </button>
              ))}
            </div>

            {selectedDepartment && (
              <div className="category-tabs" aria-label={t.aisleMetric}>
                {(selectedDepartment.children.length > 0 ? selectedDepartment.children : [selectedDepartment]).map((category) => (
                  <button
                    key={category.id}
                    className={selectedCategoryId === category.id ? "active" : ""}
                    onClick={() => {
                      setCategoryPage(1);
                      setDealPage(1);
                      setSelectedCategoryId(category.id);
                    }}
                  >
                    <strong>{translateCategoryName(category.name, language)}</strong>
                    <span>{category.totalProductCount}</span>
                  </button>
                ))}
              </div>
            )}

            {selectedCategory && <p className="hint">{formatCategoryTrail(selectedCategory, categories, language)}</p>}

            <div className="main-grid">
              <section className="table-card">
                <div className="table-heading">
                  <div>
                    <h3>{t.cheapest}</h3>
                    <span>
                      {t.pageLabel} {categoryPage} / {categoryTotalPages} · {t.showingProducts} {visibleGroups.length.toLocaleString()} / {categoryTotal.toLocaleString()} · {selectedStoreScope}
                    </span>
                  </div>
                  <div className="table-controls">
                    <button className="refresh-button" onClick={refreshCategoryProducts} disabled={isProductsLoading}>
                      {isProductsLoading ? t.loadingShort : t.refresh}
                    </button>
                    <label className="sort-control">
                      <span>{t.promoFilter}</span>
                      <select
                        value={categoryPromoFilter}
                        onChange={(event) => changeCategoryPromoFilter(event.target.value as ProductPromoFilter)}
                      >
                        {productPromoFilters.map((filter) => (
                          <option key={filter} value={filter}>
                            {formatPromoFilterLabel(filter, language)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className="sort-control">
                      <span>{t.sortBy}</span>
                      <select value={categorySort} onChange={(event) => changeCategorySort(event.target.value as ProductSort)}>
                        {(["unitPriceAsc", "priceAsc", "nameAsc", "discountDesc"] as ProductSort[]).map((sort) => (
                          <option key={sort} value={sort}>
                            {formatSortLabel(sort, language)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className="sort-control">
                      <span>{t.pageSize}</span>
                      <select value={categoryPageSize} onChange={(event) => changeCategoryPageSize(Number(event.target.value))}>
                        {categoryPageSizeOptions.map((size) => (
                          <option key={size} value={size}>
                            {size}
                          </option>
                        ))}
                      </select>
                    </label>
                  </div>
                </div>
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>{t.productName}</th>
                        <th>{t.brand}</th>
                        <th>{t.cheapestStore}</th>
                        <th>{t.price}</th>
                        <th>{t.unit}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {visibleGroups.map((group, index) => {
                        const listing = bestListing(group, selectedStores);
                        if (!listing) return null;
                        const compareLabel = compareMarkerLabel(group, language);
                        const markerClass = compareMarkerClassName(group);
                        const unmatchedClass = group.itemId === null ? " unmatched-group" : "";
                        return (
                          <tr
                            key={listing.id}
                            className={`${selectedProductId === listing.id ? "selected-row" : ""}${markerClass ? ` non-comparable ${markerClass}` : ""}${unmatchedClass}`.trim()}
                            onClick={() => setSelectedProductId(listing.id)}
                          >
                            <td>
                              <span className="rank">#{(categoryPage - 1) * categoryPageSize + index + 1}</span>
                            </td>
                            <td>
                              <div className="product-cell">
                                <ProductImage imageUrl={imageForListing(listing)} name={listing.name} size="small" />
                                <div>
                                  <strong>{listing.name}</strong>
                                  <small>{formatListingMeta(listing, group, language)}</small>
                                </div>
                              </div>
                            </td>
                            <td>{listing.brand || <span className="muted-cell">{t.noBrand}</span>}</td>
                            <td>
                              <ChainBadge chain={listing.supermarket} />
                              <span>{formatStoreBranch(listing.store, listing.supermarket, language)}</span>
                            </td>
                            <td>
                              <div className="table-price-cell">
                                <strong>{formatMaybeCurrency(listing.price)}</strong>
                                <PromotionBadges listing={listing} language={language} compact />
                              </div>
                            </td>
                            <td data-compare-label={compareLabel}>{formatUnit(listing.unitPrice, listing.unit)}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
                {visibleGroups.length === 0 && !isProductsLoading && <p className="empty-state">{t.noResults}</p>}
                <div className="pagination-bar">
                  <button onClick={() => changeCategoryPage(categoryPage - 1)} disabled={isProductsLoading || categoryPage <= 1}>
                    {t.previousPage}
                  </button>
                  <div className="page-number-group" aria-label={t.pageLabel}>
                    {paginationPages(categoryPage, categoryTotalPages).map((page, index) =>
                      page === "ellipsis" ? (
                        <span key={`ellipsis-${index}`} className="page-ellipsis">…</span>
                      ) : (
                        <button
                          key={page}
                          className={page === categoryPage ? "active" : ""}
                          onClick={() => changeCategoryPage(page)}
                          disabled={isProductsLoading || page === categoryPage}
                        >
                          {page}
                        </button>
                      )
                    )}
                  </div>
                  <button onClick={() => changeCategoryPage(categoryPage + 1)} disabled={isProductsLoading || !categoryHasMore}>
                    {t.nextPage}
                  </button>
                </div>
              </section>

              <aside
                className={`detail-card${comparison && compareMarkerClassName(comparison) ? ` non-comparable ${compareMarkerClassName(comparison)}` : ""}${
                  comparison?.itemId === null ? " unmatched-group" : ""
                }`}
                data-compare-label={comparison ? compareMarkerLabel(comparison, language) : undefined}
              >
                <div className="detail-heading">
                  <ProductImage imageUrl={imageForListing(selectedListing) ?? firstImage(comparison)} name={selectedListing?.name ?? ""} size="large" />
                  <div>
                    <p className="eyebrow">{t.compare}</p>
                    <h3>{selectedListing?.name ?? "..."}</h3>
                    {selectedListing && comparison && <p>{formatListingMeta(selectedListing, comparison, language)}</p>}
                  </div>
                </div>

                {isProductLoading && <p className="empty-state">{t.loadingShort}</p>}

                {comparison && !isProductLoading && (
                  <>
                    <div className={`compare-summary ${hasPriceGap(detailProducts) ? "" : "single"}`}>
                      <div>
                        <span>{t.cheapestStore}</span>
                        <strong>
                          {detailProducts[0]
                            ? formatFullStoreName(detailProducts[0].store, detailProducts[0].supermarket, language)
                            : "-"}
                        </strong>
                      </div>
                      <div>
                        <span>{t.priceStatus}</span>
                        <strong>{describePriceStatus(detailProducts, t)}</strong>
                      </div>
                    </div>

                    {selectedListing && shouldOfferMatchReview(comparison) && (
                      <button className="match-review-button" onClick={() => openReviewForProduct(selectedListing.id)}>
                        {t.matchThisProduct}
                      </button>
                    )}

                    <div className="price-list">
                      {detailProducts.length === 0 && <p className="empty-state">{t.noStoreData}</p>}
                      {detailProducts.map((listing) => (
                        <article key={listing.id} className="price-row">
                          <div>
                            <ChainBadge chain={listing.supermarket} />
                            <strong>{formatStoreBranch(listing.store, listing.supermarket, language)}</strong>
                          </div>
                          <div>
                            <strong>{formatMaybeCurrency(listing.price)}</strong>
                            <PromotionBadges listing={listing} language={language} compact />
                            <span>{formatUnit(listing.unitPrice, listing.unit)}</span>
                          </div>
                          <small>
                            <span>{listing.name}</span>
                            <span>{formatListingMeta(listing, comparison, language)}</span>
                            <span>{formatPriceFreshness(listing, t)}</span>
                          </small>
                        </article>
                      ))}
                    </div>

                    <div className="history-card">
                      <h4>{t.history}</h4>
                      <p>{t.historyHint}</p>
                      {history && countHistoryPoints(history, selectedStores) > 0 ? (
                        <HistoryTrendChart stores={filterHistoryStores(history.stores, selectedStores)} language={language} />
                      ) : (
                        <p>{t.noHistory}</p>
                      )}
                    </div>
                  </>
                )}
              </aside>
            </div>
          </CollapsibleSection>

          <CollapsibleSection
            className="coverage-panel"
            eyebrow={t.coverageEyebrow}
            title={t.departments}
            collapsed={collapsedSections.coverage}
            expandLabel={t.expand}
            collapseLabel={t.collapse}
            onToggle={() => toggleSection("coverage")}
          >
            <div className="department-grid">
              {categories.map((department) => (
                <article key={department.id} className="department-card">
                  <strong>{translateCategoryName(department.name, language)}</strong>
                  <span>
                    {department.totalProductCount.toLocaleString()} {t.productCount}
                  </span>
                  <small>{department.children.map((child) => translateCategoryName(child.name, language)).join(" · ")}</small>
                </article>
              ))}
            </div>
          </CollapsibleSection>
        </>
      )}
    </main>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <article className="metric-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  );
}

function paginationPages(currentPage: number, totalPages: number): Array<number | "ellipsis"> {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, index) => index + 1);

  const pages = new Set([1, totalPages, currentPage - 1, currentPage, currentPage + 1]);
  const visible = [...pages]
    .filter((page) => page >= 1 && page <= totalPages)
    .sort((a, b) => a - b);

  return visible.flatMap((page, index) => {
    const previous = visible[index - 1];
    if (previous && page - previous > 1) return ["ellipsis" as const, page];
    return [page];
  });
}

function CollapsibleSection({
  className,
  eyebrow,
  title,
  meta,
  pill,
  actions,
  collapsed,
  expandLabel,
  collapseLabel,
  onToggle,
  children
}: {
  className?: string;
  eyebrow: string;
  title: string;
  meta?: string;
  pill?: string;
  actions?: ReactNode;
  collapsed: boolean;
  expandLabel: string;
  collapseLabel: string;
  onToggle: () => void;
  children: ReactNode;
}) {
  return (
    <section className={`panel collapsible-panel ${className ?? ""}`}>
      <div className="section-heading">
        <button className="section-title-button" onClick={onToggle} aria-expanded={!collapsed}>
          <span className={`collapse-icon ${collapsed ? "" : "open"}`}>›</span>
          <span>
            <span className="eyebrow">{eyebrow}</span>
            <strong>
              {title}
              {meta && <span className="heading-date">{meta}</span>}
            </strong>
          </span>
        </button>
        <div className="section-heading-actions">
          {pill && <span className="data-pill">{pill}</span>}
          {actions}
          <button className="collapse-toggle" onClick={onToggle}>
            {collapsed ? expandLabel : collapseLabel}
          </button>
        </div>
      </div>
      {!collapsed && children}
    </section>
  );
}

function SearchResultCard({
  group,
  listing,
  listings,
  language,
  t,
  onSelect
}: {
  group: ProductGroup;
  listing: ProductListing;
  listings: ProductListing[];
  language: Language;
  t: (typeof copy)[Language];
  onSelect: () => void;
}) {
  const visibleListings = listings;

  return (
    <button
      className={`search-result${compareMarkerClassName(group) ? ` non-comparable ${compareMarkerClassName(group)}` : ""}${
        group.itemId === null ? " unmatched-group" : ""
      }`}
      data-compare-label={compareMarkerLabel(group, language)}
      onClick={onSelect}
    >
      <ProductImage imageUrl={imageForListing(listing) ?? firstImage(group)} name={listing.name} size="large" />
      <span className="search-result-body">
        <strong>{listing.name}</strong>
        <span className="search-meta">{[listing.brand, listing.size, group.category].filter(Boolean).join(" · ") || formatGroupCaption(group, language)}</span>
        <span className="search-price-labels">
          {visibleListings.map((offer, index) => (
            <span key={offer.id} className={`search-price-label ${index === 0 ? "best" : ""}`}>
              <ChainBadge chain={offer.supermarket} />
              <span className="search-offer-price">
                <strong>{formatMaybeCurrency(offer.price)}</strong>
                <PromotionBadges listing={offer} language={language} compact />
              </span>
              <span>{formatUnit(offer.unitPrice, offer.unit)}</span>
            </span>
          ))}
        </span>
        <small>
          {t.bestStore} · {listings.length} {t.listedStores}
        </small>
      </span>
    </button>
  );
}

const historyLineColors = ["#1f6b3c", "#b33a2b", "#2769ad", "#c58a00", "#7a5195", "#008c86", "#5b6c62", "#c24b88"];

function HistoryTrendChart({
  stores,
  language
}: {
  stores: ProductPriceHistory["stores"];
  language: Language;
}) {
  const chart = buildHistoryTrendChart(stores, language);
  if (!chart) return null;

  return (
    <div className="history-trend">
      <svg className="history-chart" viewBox={`0 0 ${chart.width} ${chart.height}`} role="img" aria-label="Price history">
        {chart.yTicks.map((tick) => (
          <g key={tick.label}>
            <line className="history-grid-line" x1={chart.plotLeft} x2={chart.plotRight} y1={tick.y} y2={tick.y} />
            <text className="history-axis-label" x={chart.plotLeft - 8} y={tick.y + 4} textAnchor="end">
              {tick.label}
            </text>
          </g>
        ))}
        <line className="history-axis-line" x1={chart.plotLeft} x2={chart.plotRight} y1={chart.plotBottom} y2={chart.plotBottom} />
        <text className="history-axis-label" x={chart.plotLeft} y={chart.height - 6}>
          {formatMonthDay(chart.startTime)}
        </text>
        <text className="history-axis-label" x={chart.plotRight} y={chart.height - 6} textAnchor="end">
          {language === "zh" ? "今天" : "Today"}
        </text>
        {chart.series.map((series) => (
          <g key={series.key}>
            {series.path ? (
              <path className="history-line" d={series.path} style={{ stroke: series.color }} />
            ) : null}
            {series.points.map((point) => (
              <circle
                key={`${series.key}-${point.key}`}
                className={`history-dot${point.isOnSpecial ? " special" : ""}`}
                cx={point.x}
                cy={point.y}
                r={point.isOnSpecial ? 4 : 3.4}
                style={{ stroke: series.color }}
              />
            ))}
          </g>
        ))}
      </svg>
      <div className="history-legend">
        {chart.series.map((series) => (
          <div className="history-legend-item" key={series.key}>
            <span className="history-legend-swatch" style={{ backgroundColor: series.color }} />
            <span>
              <ChainBadge chain={series.supermarket} />
              <strong>{series.storeName}</strong>
              <small>
                {formatMaybeCurrency(series.latest?.price ?? null)}
                {series.latest ? ` · ${formatShortDateTime(series.latest.date)}` : ""}
              </small>
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

type ReviewProductGroup = {
  productId: string;
  productName: string;
  brand: string | null;
  size: string | null;
  supermarket: Supermarket;
  price: number | null;
  sku: string | null;
  topScore: number;
  candidates: MatchCandidate[];
};

function MatchCoveragePanel({
  apiBaseUrl,
  language,
  refreshToken = 0,
  selectedChain,
  onSelectedChainChange
}: {
  apiBaseUrl: string;
  language: Language;
  refreshToken?: number;
  selectedChain: Supermarket;
  onSelectedChainChange: (chain: Supermarket) => void;
}) {
  const labels = matchCoverageCopy[language];
  const [report, setReport] = useState<ProductStatusReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setReport(null);
    setError(null);

    getProductStatusReport(apiBaseUrl)
      .then((result) => {
        if (!cancelled) setReport(result);
      })
      .catch(() => {
        if (!cancelled) setError(labels.failed);
      });

    return () => {
      cancelled = true;
    };
  }, [apiBaseUrl, labels.failed, refreshKey, refreshToken]);

  const rows = (report?.chains ?? []).filter(
    (row): row is ChainStatusRow & { supermarket: Supermarket } => row.supermarket !== "Total"
  );
  const selected = rows.find((row) => row.supermarket === selectedChain) ?? rows[0] ?? null;
  const isLoading = report === null && error === null;

  return (
    <section className="match-coverage" aria-label={labels.title}>
      <div className="match-coverage-heading">
        <div>
          <p className="eyebrow">{labels.title}</p>
          <strong>{labels.title}</strong>
        </div>
        <button onClick={() => setRefreshKey((current) => current + 1)} disabled={isLoading}>
          {language === "zh" ? "刷新" : "Refresh"}
        </button>
      </div>

      {isLoading && <p className="match-coverage-status">{labels.loading}</p>}
      {error && <p className="match-coverage-status error-card">{error}</p>}

      {report && (
        <>
          <div className="match-coverage-table-wrap">
            <table className="match-coverage-table">
              <thead>
                <tr>
                  <th>{labels.chain}</th>
                  <th>{labels.available}</th>
                  <th>{labels.linked}</th>
                  <th>{labels.unlinked}</th>
                  <th>{labels.coverage}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.supermarket} className={selected?.supermarket === row.supermarket ? "active" : ""}>
                    <td>
                      <button className="match-coverage-chain" onClick={() => onSelectedChainChange(row.supermarket)}>
                        <ChainBadge chain={row.supermarket} />
                        <span>{formatChain(row.supermarket)}</span>
                      </button>
                    </td>
                    <td>{row.total.toLocaleString()}</td>
                    <td>{linkedProductCount(row).toLocaleString()}</td>
                    <td className={unlinkedProductCount(row) > 0 ? "coverage-alert" : ""}>{unlinkedProductCount(row).toLocaleString()}</td>
                    <td>{formatCoveragePercent(linkedProductCount(row), row.total)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th>{labels.total}</th>
                  <th>{report.total.total.toLocaleString()}</th>
                  <th>{linkedProductCount(report.total).toLocaleString()}</th>
                  <th>{unlinkedProductCount(report.total).toLocaleString()}</th>
                  <th>{formatCoveragePercent(linkedProductCount(report.total), report.total.total)}</th>
                </tr>
              </tfoot>
            </table>
          </div>

          {selected && (
            <div className="match-coverage-detail">
              <div className="match-coverage-detail-heading">
                <span>{labels.detail}</span>
                <strong>{formatChain(selected.supermarket)}</strong>
              </div>
              <div className="match-coverage-detail-grid">
                <CoverageMetric label={labels.foodstuffsItem} value={selected.foodstuffsItem.toLocaleString()} />
                <CoverageMetric label={labels.woolworthsItem} value={selected.woolworthsItem.toLocaleString()} />
                <CoverageMetric label={labels.freshChoiceItem} value={selected.freshChoiceItem.toLocaleString()} />
                <CoverageMetric label={labels.manualItem} value={selected.manualItem.toLocaleString()} />
                <CoverageMetric
                  label={labels.pendingReview}
                  value={selected.pendingReview.toLocaleString()}
                  alert={selected.pendingReview > 0}
                  onClick={() => onSelectedChainChange(selected.supermarket)}
                />
                <CoverageMetric label={labels.held} value={selected.held.toLocaleString()} alert={selected.held > 0} />
              </div>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function CoverageMetric({
  label,
  value,
  alert = false,
  onClick
}: {
  label: string;
  value: string;
  alert?: boolean;
  onClick?: () => void;
}) {
  const className = alert ? "coverage-metric alert" : "coverage-metric";
  const content = (
    <>
      <span>{label}</span>
      <strong>{value}</strong>
    </>
  );

  return onClick ? (
    <button type="button" className={className} onClick={onClick}>
      {content}
    </button>
  ) : (
    <div className={className}>{content}</div>
  );
}

function linkedProductCount(row: ChainStatusRow) {
  return row.foodstuffsItem + row.woolworthsItem + row.freshChoiceItem + row.manualItem;
}

function unlinkedProductCount(row: ChainStatusRow) {
  return row.pendingReview + row.held;
}

function formatCoveragePercent(linked: number, available: number) {
  if (available === 0) return "-";
  return `${((linked / available) * 100).toFixed(1)}%`;
}

function ReviewWorkspaceV2({
  candidates,
  categories,
  isLoading,
  actionId,
  error,
  language,
  apiBaseUrl,
  coverageRefreshKey,
  targetProductId,
  t,
  onRefresh,
  onTargetConsumed,
  onAction
}: {
  candidates: MatchCandidate[];
  categories: CategoryNode[];
  isLoading: boolean;
  actionId: string | null;
  error: string | null;
  language: Language;
  apiBaseUrl: string;
  coverageRefreshKey: number;
  targetProductId: string | null;
  t: (typeof copy)[Language];
  onRefresh: () => void | Promise<void>;
  onTargetConsumed: () => void;
  onAction: (id: string, status: "approved" | "rejected") => void;
}) {
  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [selectedCandidateId, setSelectedCandidateId] = useState<string | null>(null);
  const [reviewSearchTerm, setReviewSearchTerm] = useState("");
  const [sourceGroup, setSourceGroup] = useState<ProductGroup | null>(null);
  const [destinationGroup, setDestinationGroup] = useState<ProductGroup | null>(null);
  const [isDetailLoading, setIsDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [reviewChain, setReviewChain] = useState<Supermarket>("Woolworths");
  const [manualItem, setManualItem] = useState({
    name: "",
    description: "",
    brand: "",
    size: "",
    category: ""
  });
  const [isCreateItemOpen, setIsCreateItemOpen] = useState(false);
  const [isCreatingItem, setIsCreatingItem] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const reviewCandidates = useMemo(
    () => candidates.filter((candidate) => candidate.supermarket === reviewChain),
    [candidates, reviewChain]
  );
  const filteredReviewCandidates = useMemo(
    () => filterMatchCandidates(reviewCandidates, reviewSearchTerm),
    [reviewCandidates, reviewSearchTerm]
  );
  const allProductGroups = useMemo(() => groupMatchCandidates(reviewCandidates), [reviewCandidates]);
  const productGroups = useMemo(() => groupMatchCandidates(filteredReviewCandidates), [filteredReviewCandidates]);
  const targetCandidate = useMemo(
    () => (targetProductId ? candidates.find((candidate) => candidate.productId === targetProductId) ?? null : null),
    [candidates, targetProductId]
  );
  const targetMissing = Boolean(targetProductId && !isLoading && candidates.length > 0 && !targetCandidate);
  const selectedProductGroup = productGroups.find((group) => group.productId === selectedProductId) ?? productGroups[0] ?? null;
  const selectedCandidate =
    selectedProductGroup?.candidates.find((candidate) => candidate.id === selectedCandidateId) ??
    selectedProductGroup?.candidates[0] ??
    null;
  const selectedCandidateCategory = selectedCandidate
    ? selectedCandidate.candidateItemCategory ?? destinationGroup?.category ?? null
    : null;
  const sourceListing = selectedCandidate
    ? sourceGroup?.products.find((product) => product.id === selectedCandidate.productId) ?? sourceGroup?.products[0] ?? null
    : null;
  const destinationListings = destinationGroup ? sortListings(destinationGroup.products) : [];
  const categoryOptions = useMemo(() => {
    const options = categories.filter((category) => category.kind !== "Department");
    return options.length > 0 ? options : categories;
  }, [categories]);

  useEffect(() => {
    if (!targetCandidate) return;
    setReviewChain(targetCandidate.supermarket);
    setReviewSearchTerm("");
    setSelectedProductId(targetCandidate.productId);
    setSelectedCandidateId(targetCandidate.id);
    onTargetConsumed();
  }, [onTargetConsumed, targetCandidate]);

  useEffect(() => {
    if (productGroups.length === 0) {
      setSelectedProductId(null);
      setSelectedCandidateId(null);
      return;
    }
    if (!selectedProductId || !productGroups.some((group) => group.productId === selectedProductId)) {
      const firstGroup = productGroups[0];
      setSelectedProductId(firstGroup.productId);
      setSelectedCandidateId(firstGroup.candidates[0]?.id ?? null);
    }
  }, [productGroups, selectedProductId]);

  useEffect(() => {
    if (!selectedProductGroup) return;
    if (!selectedCandidateId || !selectedProductGroup.candidates.some((candidate) => candidate.id === selectedCandidateId)) {
      setSelectedCandidateId(selectedProductGroup.candidates[0]?.id ?? null);
    }
  }, [selectedCandidateId, selectedProductGroup]);

  useEffect(() => {
    if (!selectedCandidate) {
      setSourceGroup(null);
      setDestinationGroup(null);
      setCreateError(null);
      return;
    }

    let cancelled = false;
    async function loadReviewDetail() {
      setIsDetailLoading(true);
      setDetailError(null);
      try {
        const [source, destinationMatches] = await Promise.all([
          getProductGroup(selectedCandidate.productId, apiBaseUrl),
          searchProducts(selectedCandidate.candidateItem, 100, [], apiBaseUrl)
        ]);
        if (cancelled) return;
        setSourceGroup(source);
        setDestinationGroup(destinationMatches.items.find((group) => group.itemId === selectedCandidate.candidateItemId) ?? null);
      } catch (err) {
        if (cancelled) return;
        setSourceGroup(null);
        setDestinationGroup(null);
        setDetailError(err instanceof Error ? err.message : t.failed);
      } finally {
        if (!cancelled) setIsDetailLoading(false);
      }
    }

    void loadReviewDetail();
    return () => {
      cancelled = true;
    };
  }, [apiBaseUrl, selectedCandidate, t.failed]);

  useEffect(() => {
    if (!selectedCandidate) return;
    const name = sourceListing?.name ?? selectedCandidate.productName;
    const category = destinationGroup?.category ?? sourceGroup?.category ?? categoryOptions[0]?.name ?? "";
    setManualItem({
      name,
      description: name,
      brand: sourceListing?.brand ?? selectedCandidate.brand ?? "",
      size: sourceListing?.size ?? selectedCandidate.size ?? "",
      category
    });
    setCreateError(null);
  }, [categoryOptions, destinationGroup?.category, selectedCandidate, sourceGroup?.category, sourceListing]);

  async function handleCreateItem(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedCandidate) return;

    const name = manualItem.name.trim();
    const category = manualItem.category.trim();
    if (!name) {
      setCreateError(t.itemNameRequired);
      return;
    }
    if (!category) {
      setCreateError(t.categoryRequired);
      return;
    }

    setIsCreatingItem(true);
    setCreateError(null);
    try {
      const item = await createItem(
        {
          name,
          description: manualItem.description.trim() || null,
          brand: manualItem.brand.trim() || null,
          size: manualItem.size.trim() || null,
          category
        },
        apiBaseUrl
      );
      await linkProductToItem(selectedCandidate.productId, item.id, apiBaseUrl);
      await onRefresh();
      setIsCreateItemOpen(false);
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : t.failed);
    } finally {
      setIsCreatingItem(false);
    }
  }

  return (
    <section className="panel review-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">{t.reviewMode}</p>
          <h2>{t.reviewTitle}</h2>
          <p className="review-intro">{t.reviewIntro}</p>
        </div>
        <div className="review-actions">
          <button onClick={onRefresh}>{t.refresh}</button>
        </div>
      </div>

      <div className="review-rules">
        <strong>{t.reviewRulesTitle}</strong>
        <ul>
          <li>{t.reviewRuleBrandSize}</li>
          <li>{t.reviewRuleVariant}</li>
          <li>{t.reviewRuleDestination}</li>
          <li>{t.reviewRuleReject}</li>
        </ul>
      </div>

      <MatchCoveragePanel
        apiBaseUrl={apiBaseUrl}
        language={language}
        refreshToken={coverageRefreshKey}
        selectedChain={reviewChain}
        onSelectedChainChange={setReviewChain}
      />

      {error && <p className="empty-state error-card">{error}</p>}
      {targetMissing && <p className="empty-state error-card">{t.reviewTargetMissing}</p>}
      <div className="review-filter-bar">
        <input
          value={reviewSearchTerm}
          onChange={(event) => setReviewSearchTerm(event.target.value)}
          placeholder={t.reviewSearchPlaceholder}
          aria-label={t.reviewSearchPlaceholder}
        />
        <span className="data-pill">
          {t.reviewSearchResult} {productGroups.length.toLocaleString()} / {allProductGroups.length.toLocaleString()}
        </span>
      </div>
      {isLoading && <p className="empty-state">{t.reviewLoading}</p>}
      {!isLoading && productGroups.length === 0 && <p className="empty-state">{t.noCandidates}</p>}

      {productGroups.length > 0 && (
        <div className="review-workspace">
          <aside className="review-product-column">
            {selectedCandidate && (
              <section className="review-source-card">
                <p className="eyebrow">{t.sourceProduct}</p>
                <ProductImage imageUrl={imageForListing(sourceListing)} name={selectedCandidate.productName} size="large" />
                <h3>{sourceListing?.name ?? selectedCandidate.productName}</h3>
                <p className="review-meta">
                  <ChainBadge chain={selectedCandidate.supermarket} />
                  <span>{sourceListing ? formatStoreBranch(sourceListing.store, sourceListing.supermarket, language) : selectedCandidate.supermarket}</span>
                  <span>{sourceListing?.brand ?? selectedCandidate.brand ?? t.noBrand}</span>
                  {(sourceListing?.size ?? selectedCandidate.size) && <span>{sourceListing?.size ?? selectedCandidate.size}</span>}
                </p>
                <p className="identity-line">{formatProductIdentity(sourceListing?.sku ?? sourceListing?.sourceSku ?? selectedCandidate.sku ?? selectedCandidate.sourceSku, selectedCandidate.productId, t)}</p>
                <strong className="match-price">{formatMaybeCurrency(sourceListing?.price ?? selectedCandidate.price)}</strong>
                {sourceListing && <small>{formatUnit(sourceListing.unitPrice, sourceListing.unit)}</small>}
              </section>
            )}

            <section className="review-queue">
              <div className="review-subheading">
                <p className="eyebrow">{t.productQueue}</p>
                <strong>{productGroups.length.toLocaleString()}</strong>
              </div>
              <div className="review-queue-list">
                {productGroups.map((group) => (
                  <button
                    key={group.productId}
                    className={group.productId === selectedProductGroup?.productId ? "active" : ""}
                    onClick={() => {
                      setSelectedProductId(group.productId);
                      setSelectedCandidateId(group.candidates[0]?.id ?? null);
                    }}
                  >
                    <span>
                      <strong>{group.productName}</strong>
                      <small>{group.brand || t.noBrand}{group.size ? ` · ${group.size}` : ""}</small>
                      <small>{formatProductIdentity(group.sku, group.productId, t)}</small>
                    </span>
                    <span className="review-score-stack">
                      <span className="review-score-pill">{formatScore(group.topScore)}</span>
                      <small>{group.candidates.length} {t.candidateCount}</small>
                    </span>
                  </button>
                ))}
              </div>
            </section>
          </aside>

          {selectedProductGroup && (
            <section className="review-candidate-column">
              <div className="review-subheading">
                <p className="eyebrow">{t.candidateItems}</p>
                <strong>{selectedProductGroup.candidates.length.toLocaleString()}</strong>
              </div>
              <div className="candidate-item-list">
                {selectedProductGroup.candidates.map((candidate) => {
                  const candidateCategory =
                    candidate.candidateItemCategory ??
                    (candidate.id === selectedCandidate?.id ? selectedCandidateCategory : null);
                  return (
                    <article
                    key={candidate.id}
                    role="button"
                    tabIndex={0}
                    className={`candidate-item-card ${candidate.id === selectedCandidate?.id ? "active" : ""}`}
                    onClick={() => setSelectedCandidateId(candidate.id)}
                    onKeyDown={(event) => {
                      if (event.key !== "Enter" && event.key !== " ") return;
                      event.preventDefault();
                      setSelectedCandidateId(candidate.id);
                    }}
                  >
                    <span className="candidate-item-card-title">
                      <strong>{candidate.candidateItem}</strong>
                      <span className="review-score-pill">{formatScore(candidate.score)}</span>
                    </span>
                    {candidateCategory && (
                      <p className="review-meta candidate-item-meta">
                        <span>{translateCategoryName(candidateCategory, language)}</span>
                      </p>
                    )}
                    <MatchReasonEvidence
                      reason={candidate.reason}
                      language={language}
                      brand={candidate.brand}
                      size={candidate.size}
                      compact
                    />
                    {candidate.id === selectedCandidate?.id && (
                      <span className="review-card-actions candidate-actions">
                        <button
                          type="button"
                          className="candidate-action secondary-button"
                          disabled={isCreatingItem || actionId === candidate.id}
                          onClick={(event) => {
                            event.stopPropagation();
                            setCreateError(null);
                            setIsCreateItemOpen(true);
                          }}
                        >
                          {t.createItemTitle}
                        </button>
                        <button
                          type="button"
                          className="candidate-action reject-button"
                          disabled={actionId === candidate.id}
                          onClick={(event) => {
                            event.stopPropagation();
                            onAction(candidate.id, "rejected");
                          }}
                        >
                          {t.reject}
                        </button>
                        <button
                          type="button"
                          className="candidate-action approve-button"
                          disabled={actionId === candidate.id}
                          onClick={(event) => {
                            event.stopPropagation();
                            onAction(candidate.id, "approved");
                          }}
                        >
                          {t.approve}
                        </button>
                      </span>
                    )}
                    </article>
                  );
                })}
              </div>
            </section>
          )}

          {selectedCandidate && (
            <section className="review-item-column">
              {isDetailLoading && <p className="empty-state">{t.detailLoading}</p>}
              {detailError && <p className="empty-state error-card">{detailError}</p>}

              <section className="destination-products review-item-products">
                <div className="review-subheading">
                  <p className="eyebrow">{t.currentItemProducts}</p>
                  <strong>{destinationListings.length.toLocaleString()}</strong>
                </div>
                {destinationListings.length === 0 ? (
                  <p className="empty-state">{t.destinationMissing}</p>
                ) : (
                  <div className="destination-product-list">
                    {destinationListings.map((listing) => (
                      <article key={listing.id} className="destination-product-row">
                        <ProductImage imageUrl={imageForListing(listing)} name={listing.name} size="medium" />
                        <div>
                          <strong>{listing.name}</strong>
                          <p className="review-meta">
                            <ChainBadge chain={listing.supermarket} />
                            <span>{formatStoreBranch(listing.store, listing.supermarket, language)}</span>
                            {listing.brand && <span>{listing.brand}</span>}
                            {listing.size && <span>{listing.size}</span>}
                          </p>
                          <p className="identity-line">{formatProductIdentity(listing.sku ?? listing.sourceSku, listing.id, t)}</p>
                        </div>
                        <div className="destination-price">
                          <strong>{formatMaybeCurrency(listing.price)}</strong>
                          <small>{formatUnit(listing.unitPrice, listing.unit)}</small>
                        </div>
                      </article>
                    ))}
                  </div>
                )}
              </section>
            </section>
          )}

          {selectedCandidate && isCreateItemOpen && (
            <div className="modal-backdrop" onMouseDown={() => setIsCreateItemOpen(false)}>
              <section
                className="create-item-modal"
                role="dialog"
                aria-modal="true"
                aria-labelledby="create-item-title"
                onMouseDown={(event) => event.stopPropagation()}
              >
                <div className="modal-heading">
                  <div>
                    <p className="eyebrow">{t.createItemTitle}</p>
                    <h3 id="create-item-title">{t.createItemTitle}</h3>
                    <p>{t.createItemHint}</p>
                  </div>
                  <button className="modal-close" type="button" onClick={() => setIsCreateItemOpen(false)}>
                    {language === "zh" ? "关闭" : "Close"}
                  </button>
                </div>
                <form className="create-item-form" onSubmit={handleCreateItem}>
                  <label className="form-field wide">
                    <span>{t.createItemName}</span>
                    <input
                      value={manualItem.name}
                      onChange={(event) => setManualItem((current) => ({ ...current, name: event.target.value }))}
                    />
                  </label>
                  <label className="form-field wide">
                    <span>{t.createItemDescription}</span>
                    <input
                      value={manualItem.description}
                      onChange={(event) => setManualItem((current) => ({ ...current, description: event.target.value }))}
                    />
                  </label>
                  <label className="form-field">
                    <span>{t.createItemBrand}</span>
                    <input
                      value={manualItem.brand}
                      onChange={(event) => setManualItem((current) => ({ ...current, brand: event.target.value }))}
                    />
                  </label>
                  <label className="form-field">
                    <span>{t.createItemSize}</span>
                    <input
                      value={manualItem.size}
                      onChange={(event) => setManualItem((current) => ({ ...current, size: event.target.value }))}
                    />
                  </label>
                  <label className="form-field wide">
                    <span>{t.createItemCategory}</span>
                    <select
                      value={manualItem.category}
                      onChange={(event) => setManualItem((current) => ({ ...current, category: event.target.value }))}
                    >
                      <option value="">{t.categoryRequired}</option>
                      {categoryOptions.map((category) => (
                        <option key={category.id} value={category.name}>
                          {formatReviewCategoryOption(category, language)}
                        </option>
                      ))}
                    </select>
                  </label>
                  {createError && <p className="create-item-error">{createError}</p>}
                  <div className="create-item-actions">
                    <button type="button" onClick={() => setIsCreateItemOpen(false)}>
                      {language === "zh" ? "取消" : "Cancel"}
                    </button>
                    <button className="approve-button" disabled={isCreatingItem || actionId === selectedCandidate.id}>
                      {t.createAndLink}
                    </button>
                  </div>
                </form>
              </section>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

function ReviewWorkspace({
  candidates,
  categories,
  isLoading,
  actionId,
  error,
  woolworthsOnly,
  totalCount,
  language,
  apiBaseUrl,
  t,
  onRefresh,
  onToggleWoolworthsOnly,
  onAction
}: {
  candidates: MatchCandidate[];
  categories: CategoryNode[];
  isLoading: boolean;
  actionId: string | null;
  error: string | null;
  woolworthsOnly: boolean;
  totalCount: number;
  language: Language;
  apiBaseUrl: string;
  t: (typeof copy)[Language];
  onRefresh: () => void | Promise<void>;
  onToggleWoolworthsOnly: () => void;
  onAction: (id: string, status: "approved" | "rejected") => void;
}) {
  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [selectedCandidateId, setSelectedCandidateId] = useState<string | null>(null);
  const [sourceGroup, setSourceGroup] = useState<ProductGroup | null>(null);
  const [destinationGroup, setDestinationGroup] = useState<ProductGroup | null>(null);
  const [isDetailLoading, setIsDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [manualItem, setManualItem] = useState({
    name: "",
    description: "",
    brand: "",
    size: "",
    category: ""
  });
  const [isCreatingItem, setIsCreatingItem] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const candidateGroups = useMemo(() => groupMatchCandidates(candidates), [candidates]);
  const selectedProductGroup = candidateGroups.find((group) => group.productId === selectedProductId) ?? candidateGroups[0] ?? null;
  const selectedCandidate =
    selectedProductGroup?.candidates.find((candidate) => candidate.id === selectedCandidateId) ??
    selectedProductGroup?.candidates[0] ??
    null;
  const sourceListing = selectedCandidate
    ? sourceGroup?.products.find((product) => product.id === selectedCandidate.productId) ?? sourceGroup?.products[0] ?? null
    : null;
  const destinationListings = destinationGroup ? sortListings(destinationGroup.products) : [];
  const categoryOptions = useMemo(() => {
    const options = categories.filter((category) => category.kind !== "Department");
    return options.length > 0 ? options : categories;
  }, [categories]);

  useEffect(() => {
    if (candidateGroups.length === 0) {
      setSelectedProductId(null);
      setSelectedCandidateId(null);
      return;
    }
    if (!selectedProductId || !candidateGroups.some((group) => group.productId === selectedProductId)) {
      const firstGroup = candidateGroups[0];
      setSelectedProductId(firstGroup.productId);
      setSelectedCandidateId(firstGroup.candidates[0]?.id ?? null);
    }
  }, [candidateGroups, selectedProductId]);

  useEffect(() => {
    if (!selectedProductGroup) return;
    if (!selectedCandidateId || !selectedProductGroup.candidates.some((candidate) => candidate.id === selectedCandidateId)) {
      setSelectedCandidateId(selectedProductGroup.candidates[0]?.id ?? null);
    }
  }, [selectedCandidateId, selectedProductGroup]);

  useEffect(() => {
    if (!selectedCandidate) {
      setSourceGroup(null);
      setDestinationGroup(null);
      setCreateError(null);
      return;
    }

    let cancelled = false;
    async function loadReviewDetail() {
      setIsDetailLoading(true);
      setDetailError(null);
      try {
        const [source, destinationMatches] = await Promise.all([
          getProductGroup(selectedCandidate.productId, apiBaseUrl),
          searchProducts(selectedCandidate.candidateItem, 100, [], apiBaseUrl)
        ]);
        if (cancelled) return;
        setSourceGroup(source);
        setDestinationGroup(destinationMatches.items.find((group) => group.itemId === selectedCandidate.candidateItemId) ?? null);
      } catch (err) {
        if (cancelled) return;
        setSourceGroup(null);
        setDestinationGroup(null);
        setDetailError(err instanceof Error ? err.message : t.failed);
      } finally {
        if (!cancelled) setIsDetailLoading(false);
      }
    }

    void loadReviewDetail();
    return () => {
      cancelled = true;
    };
  }, [apiBaseUrl, selectedCandidate, t.failed]);

  useEffect(() => {
    if (!selectedCandidate) return;
    const name = sourceListing?.name ?? selectedCandidate.productName;
    const category = destinationGroup?.category ?? sourceGroup?.category ?? categoryOptions[0]?.name ?? "";
    setManualItem({
      name,
      description: name,
      brand: sourceListing?.brand ?? selectedCandidate.brand ?? "",
      size: sourceListing?.size ?? selectedCandidate.size ?? "",
      category
    });
    setCreateError(null);
  }, [categoryOptions, destinationGroup?.category, selectedCandidate, sourceGroup?.category, sourceListing]);

  async function handleCreateItem(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selectedCandidate) return;

    const name = manualItem.name.trim();
    const category = manualItem.category.trim();
    if (!name) {
      setCreateError(t.itemNameRequired);
      return;
    }
    if (!category) {
      setCreateError(t.categoryRequired);
      return;
    }

    setIsCreatingItem(true);
    setCreateError(null);
    try {
      const item = await createItem(
        {
          name,
          description: manualItem.description.trim() || null,
          brand: manualItem.brand.trim() || null,
          size: manualItem.size.trim() || null,
          category
        },
        apiBaseUrl
      );
      await linkProductToItem(selectedCandidate.productId, item.id, apiBaseUrl);
      await onRefresh();
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : t.failed);
    } finally {
      setIsCreatingItem(false);
    }
  }

  return (
    <section className="panel review-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">{t.reviewMode}</p>
          <h2>{t.reviewTitle}</h2>
          <p className="review-intro">{t.reviewIntro}</p>
        </div>
        <div className="review-actions">
          <button className={woolworthsOnly ? "active" : ""} onClick={onToggleWoolworthsOnly}>
            {woolworthsOnly ? t.woolworthsOnly : t.allCandidates}
          </button>
          <button onClick={onRefresh}>{t.refresh}</button>
        </div>
      </div>

      <div className="review-rules">
        <strong>{t.reviewRulesTitle}</strong>
        <ul>
          <li>{t.reviewRuleBrandSize}</li>
          <li>{t.reviewRuleVariant}</li>
          <li>{t.reviewRuleDestination}</li>
          <li>{t.reviewRuleReject}</li>
        </ul>
      </div>

      <div className="review-summary">
        <Metric label={t.allCandidates} value={totalCount.toLocaleString()} />
        <Metric label={woolworthsOnly ? t.woolworthsOnly : t.reviewMode} value={candidates.length.toLocaleString()} />
      </div>

      {error && <p className="empty-state error-card">{error}</p>}
      {isLoading && <p className="empty-state">{t.reviewLoading}</p>}
      {!isLoading && candidates.length === 0 && <p className="empty-state">{t.noCandidates}</p>}

      {candidates.length > 0 && (
        <div className="review-workspace">
          <aside className="review-queue">
            <div className="review-subheading">
              <p className="eyebrow">{t.candidateQueue}</p>
              <strong>{candidates.length.toLocaleString()}</strong>
            </div>
            <div className="review-queue-list">
              {candidates.map((candidate) => (
                <button
                  key={candidate.id}
                  className={candidate.id === selectedCandidate?.id ? "active" : ""}
                  onClick={() => setSelectedCandidateId(candidate.id)}
                >
                  <span>
                    <strong>{candidate.productName}</strong>
                    <small>{candidate.brand || "-"}{candidate.size ? ` · ${candidate.size}` : ""}</small>
                    <small>{formatProductIdentity(candidate.sku ?? candidate.sourceSku, candidate.productId, t)}</small>
                  </span>
                  <span className="review-score-pill">{formatScore(candidate.score)}</span>
                </button>
              ))}
            </div>
          </aside>

          {selectedCandidate && (
            <article className="review-detail">
              <div className="review-detail-heading">
                <div>
                  <p className="eyebrow">{t.selectedCandidate}</p>
                  <h3>{selectedCandidate.productName}</h3>
                </div>
                <span className="review-score-pill strong">{t.matchScore} {formatScore(selectedCandidate.score)}</span>
              </div>

              {isDetailLoading && <p className="empty-state">{t.detailLoading}</p>}
              {detailError && <p className="empty-state error-card">{detailError}</p>}

              <div className="match-compare-grid">
                <section className="match-side-card source">
                  <p className="eyebrow">{t.sourceProduct}</p>
                  <div className="match-product-hero">
                    <ProductImage imageUrl={imageForListing(sourceListing)} name={selectedCandidate.productName} size="large" />
                    <div>
                      <h3>{sourceListing?.name ?? selectedCandidate.productName}</h3>
                      <p className="review-meta">
                        <ChainBadge chain={selectedCandidate.supermarket} />
                        <span>{sourceListing ? formatStoreBranch(sourceListing.store, sourceListing.supermarket, language) : selectedCandidate.supermarket}</span>
                        <span>{sourceListing?.brand ?? selectedCandidate.brand ?? "-"}</span>
                        {(sourceListing?.size ?? selectedCandidate.size) && <span>{sourceListing?.size ?? selectedCandidate.size}</span>}
                      </p>
                      <p className="identity-line">{formatProductIdentity(sourceListing?.sku ?? sourceListing?.sourceSku ?? selectedCandidate.sku ?? selectedCandidate.sourceSku, selectedCandidate.productId, t)}</p>
                      <strong className="match-price">{formatMaybeCurrency(sourceListing?.price ?? selectedCandidate.price)}</strong>
                      {sourceListing && <small>{formatUnit(sourceListing.unitPrice, sourceListing.unit)}</small>}
                    </div>
                  </div>
                </section>

                <section className="match-side-card destination">
                  <p className="eyebrow">{t.destinationItem}</p>
                  <h3>{destinationGroup?.description ?? selectedCandidate.candidateItem}</h3>
                  <p className="review-meta">
                    <span>{destinationGroup?.category ?? t.category}</span>
                    <span>{formatMatchReason(selectedCandidate.reason, language)}</span>
                  </p>
                  <div className="review-reason compact">
                    <strong>{t.matchReason}</strong>
                    <span>{formatMatchReason(selectedCandidate.reason, language)}</span>
                  </div>
                </section>
              </div>

              <section className="destination-products">
                <div className="review-subheading">
                  <p className="eyebrow">{t.currentItemProducts}</p>
                  <strong>{destinationListings.length.toLocaleString()}</strong>
                </div>
                {destinationListings.length === 0 ? (
                  <p className="empty-state">{t.destinationMissing}</p>
                ) : (
                  <div className="destination-product-list">
                    {destinationListings.map((listing) => (
                      <article key={listing.id} className="destination-product-row">
                        <ProductImage imageUrl={imageForListing(listing)} name={listing.name} size="small" />
                        <div>
                          <strong>{listing.name}</strong>
                          <p className="review-meta">
                            <ChainBadge chain={listing.supermarket} />
                            <span>{formatStoreBranch(listing.store, listing.supermarket, language)}</span>
                            {listing.brand && <span>{listing.brand}</span>}
                            {listing.size && <span>{listing.size}</span>}
                          </p>
                          <p className="identity-line">{formatProductIdentity(listing.sku ?? listing.sourceSku, listing.id, t)}</p>
                        </div>
                        <div className="destination-price">
                          <strong>{formatMaybeCurrency(listing.price)}</strong>
                          <small>{formatUnit(listing.unitPrice, listing.unit)}</small>
                        </div>
                      </article>
                    ))}
                  </div>
                )}
              </section>

              <section className="create-item-panel">
                <div>
                  <p className="eyebrow">{t.createItemTitle}</p>
                  <p>{t.createItemHint}</p>
                </div>
                <form className="create-item-form" onSubmit={handleCreateItem}>
                  <label className="form-field wide">
                    <span>{t.createItemName}</span>
                    <input
                      value={manualItem.name}
                      onChange={(event) => setManualItem((current) => ({ ...current, name: event.target.value }))}
                    />
                  </label>
                  <label className="form-field wide">
                    <span>{t.createItemDescription}</span>
                    <input
                      value={manualItem.description}
                      onChange={(event) => setManualItem((current) => ({ ...current, description: event.target.value }))}
                    />
                  </label>
                  <label className="form-field">
                    <span>{t.createItemBrand}</span>
                    <input
                      value={manualItem.brand}
                      onChange={(event) => setManualItem((current) => ({ ...current, brand: event.target.value }))}
                    />
                  </label>
                  <label className="form-field">
                    <span>{t.createItemSize}</span>
                    <input
                      value={manualItem.size}
                      onChange={(event) => setManualItem((current) => ({ ...current, size: event.target.value }))}
                    />
                  </label>
                  <label className="form-field wide">
                    <span>{t.createItemCategory}</span>
                    <select
                      value={manualItem.category}
                      onChange={(event) => setManualItem((current) => ({ ...current, category: event.target.value }))}
                    >
                      <option value="">{t.categoryRequired}</option>
                      {categoryOptions.map((category) => (
                        <option key={category.id} value={category.name}>
                          {formatReviewCategoryOption(category, language)}
                        </option>
                      ))}
                    </select>
                  </label>
                  {createError && <p className="create-item-error">{createError}</p>}
                  <div className="create-item-actions">
                    <button className="approve-button" disabled={isCreatingItem || actionId === selectedCandidate.id}>
                      {t.createAndLink}
                    </button>
                  </div>
                </form>
              </section>

              <footer className="review-card-actions">
                <button
                  className="reject-button"
                  disabled={actionId === selectedCandidate.id}
                  onClick={() => onAction(selectedCandidate.id, "rejected")}
                >
                  {t.reject}
                </button>
                <button
                  className="approve-button"
                  disabled={actionId === selectedCandidate.id}
                  onClick={() => onAction(selectedCandidate.id, "approved")}
                >
                  {t.approve}
                </button>
              </footer>
            </article>
          )}
        </div>
      )}
    </section>
  );
}

function ProductImage({
  imageUrl,
  name,
  size
}: {
  imageUrl?: string | null;
  name: string;
  size: "small" | "medium" | "large";
}) {
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
  }, [imageUrl]);

  if (!imageUrl || failed) {
    return (
      <div className={`product-image product-image-${size} product-image-empty`} aria-label="No product image">
        <span>{name ? name.slice(0, 1).toUpperCase() : "-"}</span>
      </div>
    );
  }

  return (
    <img
      className={`product-image product-image-${size}`}
      src={imageUrl}
      alt={name}
      loading="lazy"
      referrerPolicy="no-referrer"
      onError={() => setFailed(true)}
    />
  );
}

function imageForListing(listing: ProductListing | null | undefined) {
  if (!listing) return null;
  return listing.imageUrl ?? foodstuffsImageUrl(listing.supermarket, listing.sku ?? listing.sourceSku);
}

function imageForDeal(deal: DealItem) {
  return deal.imageUrl ?? foodstuffsImageUrl(deal.supermarket, deal.sku);
}

function foodstuffsImageUrl(supermarket: Supermarket, sku: string | null | undefined) {
  if ((supermarket !== "NewWorld" && supermarket !== "PaknSave") || !sku) return null;
  const productId = sku.match(/^\d+/)?.[0];
  return productId ? `https://a.fsimg.co.nz/product/retail/fan/image/400x400/${productId}.png` : null;
}

function ChainBadge({ chain }: { chain: Supermarket }) {
  return <span className={`chain-badge ${chainClass[chain]}`}>{formatChain(chain)}</span>;
}

function StoreIdentity({ store, chain, language }: { store: string; chain: Supermarket; language: Language }) {
  return (
    <span className="store-identity">
      <ChainBadge chain={chain} />
      <strong>{formatStoreBranch(store, chain, language)}</strong>
    </span>
  );
}

function PromotionBadges({
  listing,
  point,
  language,
  compact = false
}: {
  listing?: ProductListing | null;
  point?: ProductPriceHistory["stores"][number]["points"][number] | null;
  language: Language;
  compact?: boolean;
}) {
  const promoType = listing?.promoType ?? point?.promoType ?? null;
  const memberPrice = listing?.memberPrice ?? point?.memberPrice ?? null;
  const wasPrice = listing?.wasPrice ?? point?.wasPrice ?? null;
  const multibuyQuantity = listing?.multibuyQuantity ?? null;
  const multibuyTotal = listing?.multibuyTotal ?? null;
  const hasMultibuy = multibuyQuantity !== null && multibuyQuantity > 1 && multibuyTotal !== null;
  const multibuyOffer = hasMultibuy
    ? language === "zh"
      ? `${multibuyQuantity} 件 ${formatCurrency(multibuyTotal)}`
      : `${multibuyQuantity} for ${formatCurrency(multibuyTotal)}`
    : null;
  const labels =
    language === "zh"
      ? { special: "特价", was: "原价", member: "会员价", memberMultibuy: "会员多件价", multibuy: "多件价" }
      : { special: "Special", was: "was", member: "Member", memberMultibuy: "Member multibuy", multibuy: "Multibuy" };

  if (!promoType && !hasMultibuy) return null;

  return (
    <span className={`promotion-badges${compact ? " compact" : ""}`}>
      {promoType === "Special" && (
        <span className="promotion-badge promotion-special">
          {labels.special}
          {wasPrice !== null ? ` · ${labels.was} ${formatPromotionWasPrice(wasPrice, listing)}` : ""}
        </span>
      )}
      {promoType === "MemberPrice" && memberPrice !== null && (
        <span className="promotion-badge promotion-member">
          {labels.member} {formatCurrency(memberPrice)}
        </span>
      )}
      {hasMultibuy && (
        <span className={`promotion-badge ${promoType === "MemberPrice" ? "promotion-member" : "promotion-multibuy"}`}>
          {promoType === "MemberPrice" ? labels.memberMultibuy : labels.multibuy} · {multibuyOffer}
        </span>
      )}
      {promoType === "MemberPrice" && memberPrice === null && !hasMultibuy && (
        <span className="promotion-badge promotion-member">{labels.member}</span>
      )}
      {promoType === "Multibuy" && !hasMultibuy && (
        <span className="promotion-badge promotion-multibuy">{labels.multibuy}</span>
      )}
    </span>
  );
}

async function getAllDealsForRanking(apiBaseUrl: string, storeIds: string[]) {
  const size = 100;
  const maxPages = 100;
  const rows: DealItem[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const result = await getDeals(page, size, apiBaseUrl, undefined, storeIds);
    rows.push(...result.items);
    if (!result.hasMore) break;
  }

  return rows;
}

async function getAllProductGroupsForFilter(
  categoryId: string | undefined,
  sort: ProductSort,
  storeIds: string[],
  apiBaseUrl: string
) {
  const size = 100;
  const maxPages = 100;
  const rows: ProductGroup[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const result = categoryId
      ? await getCategoryProducts(categoryId, page, size, sort, storeIds, apiBaseUrl)
      : await getProducts(page, size, sort, storeIds, apiBaseUrl);
    rows.push(...result.items);
    if (!result.hasMore) break;
  }

  return rows;
}

function prepareProductGroups(groups: ProductGroup[], selectedStores: StoreOption[]) {
  return groups
    .map((group) => ({
      ...group,
      products: filterListingsByStores(group.products, selectedStores)
    }))
    .filter((group) => group.products.length > 0);
}

function isGroupComparable(group: ProductGroup) {
  return group.comparable === true;
}

function compareMarkerClassName(group: ProductGroup) {
  if (group.comparable !== false) return "";
  return "single-store";
}

function shouldOfferMatchReview(group: ProductGroup | null | undefined) {
  return group?.itemId === null || group?.comparable === false;
}

function compareMarkerLabel(group: ProductGroup, language: Language) {
  if (group.comparable !== false) return "";
  return language === "zh" ? "单店" : "Single";
}

function filterGroupsByPromo(groups: ProductGroup[], filter: ProductPromoFilter, selectedStores: StoreOption[]) {
  if (filter === "all") return groups;

  return groups
    .map((group) => {
      const products = filterListingsByStores(group.products, selectedStores).filter((listing) => listingMatchesPromoFilter(listing, filter));
      return { ...group, products };
    })
    .filter((group) => group.products.length > 0);
}

function listingMatchesPromoFilter(listing: ProductListing, filter: ProductPromoFilter) {
  if (filter === "special") return listing.promoType === "Special" || listing.isOnSpecial;
  if (filter === "member") return listing.promoType === "MemberPrice" || listing.memberPrice !== null;
  if (filter === "multibuy") {
    return listing.promoType === "Multibuy" || (listing.multibuyQuantity !== null && listing.multibuyQuantity > 1 && listing.multibuyTotal !== null);
  }
  return true;
}

function rankProductGroups(groups: ProductGroup[], selectedStores: StoreOption[]) {
  return [...groups]
    .map((group) => ({ group, listing: bestListing(group, selectedStores) }))
    .filter((entry): entry is { group: ProductGroup; listing: ProductListing } => entry.listing !== null)
    .sort((a, b) => listingSortValue(a.listing) - listingSortValue(b.listing))
    .map((entry) => ({
      ...entry.group,
      products: sortListings(filterListingsByStores(entry.group.products, selectedStores))
    }));
}

function bestListing(group: ProductGroup | undefined, selectedStores: StoreOption[]) {
  if (!group) return null;
  return sortListings(filterListingsByStores(group.products, selectedStores))[0] ?? null;
}

function sortListings(listings: ProductListing[]) {
  return [...listings].sort((a, b) => listingSortValue(a) - listingSortValue(b));
}

function listingSortValue(listing: ProductListing) {
  return listing.unitPrice ?? listing.price ?? Number.POSITIVE_INFINITY;
}

function filterListingsByStores(listings: ProductListing[], selectedStores: StoreOption[]) {
  if (selectedStores.length === 0) return sortListings(listings);
  return sortListings(listings.filter((listing) => selectedStores.some((store) => sameStore(store, listing.store, listing.supermarket))));
}

function sortDeals(deals: DealItem[], mode: DealShowcaseMode) {
  return [...deals]
    .filter((deal) => deal.price !== null)
    .sort((a, b) => {
      if (mode === "saving") {
        const saving = realPurchaseSaving(b) - realPurchaseSaving(a);
        if (saving !== 0) return saving;
        return dealDiscountPercent(b) - dealDiscountPercent(a);
      }
      const percent = dealDiscountPercent(b) - dealDiscountPercent(a);
      if (percent !== 0) return percent;
      return (b.saving ?? 0) - (a.saving ?? 0);
    });
}

function isShowcaseDeal(deal: DealItem, mode: DealShowcaseMode, percentThreshold: number, savingThreshold: number) {
  const percent = dealDiscountPercent(deal);
  const saving = deal.saving ?? 0;
  if (mode === "saving") return realPurchaseSaving(deal) >= savingThreshold;
  return percent >= percentThreshold && saving >= dealMinSaving;
}

function dealDiscountPercent(deal: DealItem) {
  if (!deal.wasPrice || !deal.saving || deal.wasPrice <= 0) return 0;
  return deal.saving / deal.wasPrice;
}

function realPurchaseSaving(deal: DealItem) {
  return isVariableWeightDeal(deal) ? 0 : deal.saving ?? 0;
}

function isVariableWeightDeal(deal: DealItem) {
  if (deal.price === null || deal.unitPrice === null) return false;
  return isVariableWeightPrice(deal.price, deal.unitPrice, deal.unitOfMeasure);
}

function isVariableWeightListing(listing: ProductListing) {
  if (listing.price === null || listing.unitPrice === null) return false;
  return isVariableWeightPrice(listing.price, listing.unitPrice, listing.unit);
}

function isVariableWeightPrice(price: number, unitPrice: number, unitOfMeasure: string | null | undefined) {
  const unit = unitOfMeasure?.toLowerCase();
  const isBulkUnit = unit === "1kg" || unit === "1l";
  return isBulkUnit && Math.abs(price - unitPrice) < 0.001;
}

function formatUnitSuffix(unit: string | null | undefined) {
  const normalized = unit?.toLowerCase();
  if (normalized === "1kg") return "/kg";
  if (normalized === "1l") return "/L";
  return "";
}

function formatReadableUnitSuffix(unit: string | null | undefined) {
  const normalized = unit?.toLowerCase();
  if (normalized === "1kg") return "kg";
  if (normalized === "1l") return "L";
  return "";
}

function formatPercentThreshold(value: number) {
  return `${Math.round(value * 100)}%`;
}

function formatSavingThreshold(value: number) {
  return `$${value}`;
}

function formatStoreScope(
  stores: StoreOption[],
  language: Language,
  allStoresLabel: string,
  selectedStoresLabel: string
) {
  if (stores.length === 0) return allStoresLabel;
  if (stores.length <= 3) {
    return stores.map((store) => formatFullStoreName(store.name, store.supermarket, language)).join(", ");
  }
  return language === "zh" ? `${selectedStoresLabel} ${stores.length} 家门店` : `${selectedStoresLabel} ${stores.length} stores`;
}

function formatWeightSoldNotice(listing: ProductListing | null, language: Language) {
  if (!listing || listing.size || listing.unit !== "1kg") return null;
  if (language === "zh") return "称重商品：源站只发布每公斤价格，没有固定包重；实际重量以门店/源站结账为准。";
  return "Sold by weight: the source publishes a per-kg price, not a fixed pack weight. Actual pack weight depends on the store/source checkout.";
}

function isStoreStaleForDeals(store: StoreOption, freshnessHours: number) {
  if (!store.lastCrawledAt) return true;
  const crawledAt = new Date(store.lastCrawledAt).getTime();
  if (Number.isNaN(crawledAt)) return true;
  return Date.now() - crawledAt > freshnessHours * 60 * 60 * 1000;
}

function formatStaleDealsMessage(stores: StoreOption[], language: Language, freshnessHours: number) {
  const storeNames = stores.map((store) => formatFullStoreName(store.name, store.supermarket, language)).join(", ");
  const crawlDates = stores.map((store) => store.lastCrawledAt).filter((value): value is string => Boolean(value));
  const latestCrawl = latestDate(crawlDates);
  const latestText = latestCrawl ? formatShortDateTime(latestCrawl.toISOString()) : language === "zh" ? "未知" : "unknown";
  if (language === "zh") {
    return `${storeNames} 数据最后更新于 ${latestText}，今日特价只展示 ${freshnessHours} 小时内确认过的价格。分类里的特价标记可能仍是旧字段，请先跑最新爬虫。`;
  }
  return `${storeNames} was last crawled at ${latestText}. Today's specials only show prices confirmed in the last ${freshnessHours} hours. Category promo badges may still be older current fields; run the latest crawler first.`;
}

function filterHistoryStores(stores: ProductPriceHistory["stores"], selectedStores: StoreOption[]) {
  if (selectedStores.length === 0) return stores;
  return stores.filter((row) => selectedStores.some((store) => sameStore(store, row.store, row.supermarket as Supermarket)));
}

function sameStore(store: StoreOption, listingStore: string, listingSupermarket: string) {
  return store.supermarket === listingSupermarket && normalizeStoreName(store.name) === normalizeStoreName(listingStore);
}

function normalizeStoreName(value: string) {
  return value.trim().toLowerCase();
}

function findListingById(groups: ProductGroup[], productId: string) {
  for (const group of groups) {
    const listing = group.products.find((product) => product.id === productId);
    if (listing) return listing;
  }
  return null;
}

function firstImage(group: ProductGroup | null) {
  const product = group?.products.find((listing) => imageForListing(listing));
  return product ? imageForListing(product) : null;
}

function groupMatchCandidates(candidates: MatchCandidate[]): ReviewProductGroup[] {
  const groups = new Map<string, ReviewProductGroup>();
  candidates.forEach((candidate) => {
    const existing = groups.get(candidate.productId);
    if (existing) {
      existing.candidates.push(candidate);
      existing.topScore = Math.max(existing.topScore, candidate.score);
      return;
    }
    groups.set(candidate.productId, {
      productId: candidate.productId,
      productName: candidate.productName,
      brand: candidate.brand,
      size: candidate.size,
      supermarket: candidate.supermarket,
      price: candidate.price,
      sku: candidate.sku ?? candidate.sourceSku ?? null,
      topScore: candidate.score,
      candidates: [candidate]
    });
  });

  return [...groups.values()].sort((a, b) => {
    const score = b.topScore - a.topScore;
    if (score !== 0) return score;
    const count = b.candidates.length - a.candidates.length;
    if (count !== 0) return count;
    return a.productName.localeCompare(b.productName);
  });
}

function filterMatchCandidates(candidates: MatchCandidate[], query: string) {
  const term = query.trim().toLowerCase();
  if (!term) return candidates;

  return candidates.filter((candidate) => {
    const fields = [
      candidate.productName,
      candidate.brand,
      candidate.size,
      candidate.sku,
      candidate.sourceSku,
      candidate.supermarket,
      candidate.candidateItem,
      candidate.candidateItemCategory,
      candidate.reason
    ];
    return fields.some((field) => field?.toLowerCase().includes(term));
  });
}

function formatProductIdentity(sku: string | null | undefined, _productId: string, t: (typeof copy)[Language]) {
  if (sku) return `${t.sku}: ${sku}`;
  return t.skuMissing;
}

function formatSortLabel(sort: ProductSort, language: Language) {
  const labels: Record<ProductSort, Record<Language, string>> = {
    unitPriceAsc: { zh: "单位价最低", en: "Lowest unit price" },
    priceAsc: { zh: "标价最低", en: "Lowest shelf price" },
    nameAsc: { zh: "名称 A-Z", en: "Name A-Z" },
    discountDesc: { zh: "省钱最多", en: "Biggest saving" }
  };
  return labels[sort][language];
}

function formatPromoFilterLabel(filter: ProductPromoFilter, language: Language) {
  const labels: Record<ProductPromoFilter, Record<Language, string>> = {
    all: { zh: "全部", en: "All" },
    special: { zh: "特价", en: "Special" },
    member: { zh: "会员价", en: "Member price" },
    multibuy: { zh: "多件价", en: "Multibuy" }
  };
  return labels[filter][language];
}

function MatchReasonEvidence({
  reason,
  language,
  brand,
  size,
  compact = false
}: {
  reason: string | null | undefined;
  language: Language;
  brand: string | null | undefined;
  size: string | null | undefined;
  compact?: boolean;
}) {
  const evidence = formatMatchEvidence(reason, language, brand, size);
  return (
    <ul className={`match-evidence${compact ? " compact" : ""}`}>
      {evidence.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  );
}

function cleanMatchReason(reason: string | null | undefined) {
  if (!reason?.trim()) return "-";

  return reason
    .replace(/;\s*ambiguous\s*\(\d+\s*candidates?\)/gi, "")
    .replace(/\s*ambiguous\s*\(\d+\s*candidates?\)/gi, "")
    .trim();
}

function formatMatchEvidence(reason: string | null | undefined, language: Language, brand?: string | null, size?: string | null) {
  const normalized = cleanMatchReason(reason);
  if (normalized === "-") return ["-"];

  const pieces: string[] = [];
  if (/brand\+size match/i.test(normalized)) {
    pieces.push(
      brand
        ? language === "zh"
          ? `品牌同为 ${brand}`
          : `Same brand: ${brand}`
        : language === "zh"
          ? "品牌一致"
          : "Brand matches"
    );
    pieces.push(
      size
        ? language === "zh"
          ? `规格同为 ${size}`
          : `Same size: ${size}`
        : language === "zh"
          ? "规格一致"
          : "Size matches"
    );
  } else {
    if (/\bbrand\b/i.test(normalized)) {
      pieces.push(
        brand
          ? language === "zh"
            ? `品牌同为 ${brand}`
            : `Same brand: ${brand}`
          : language === "zh"
            ? "品牌一致"
            : "Brand matches"
      );
    }
    if (/\bsize\b/i.test(normalized)) {
      pieces.push(
        size
          ? language === "zh"
            ? `规格同为 ${size}`
            : `Same size: ${size}`
          : language === "zh"
            ? "规格一致"
            : "Size matches"
      );
    }
  }

  const overlap = normalized.match(/name overlap\s+([0-9.]+)/i);
  if (overlap) {
    const score = Number.parseFloat(overlap[1]);
    const display = Number.isFinite(score) ? `${Math.round(score * 100)}%` : overlap[1];
    pieces.push(language === "zh" ? `名称重合 ${display}` : `Name overlap: ${display}`);
  }

  const remainder = normalized
    .replace(/brand\+size match;?/gi, "")
    .replace(/name overlap\s+[0-9.]+;?/gi, "")
    .replace(/;+$/g, "")
    .trim();

  if (remainder && pieces.length === 0) pieces.push(remainder);
  if (remainder && pieces.length > 0 && !pieces.includes(remainder)) pieces.push(remainder);
  return pieces.length > 0 ? pieces : ["-"];
}

function formatMatchReason(reason: string | null | undefined, language: Language) {
  const normalized = cleanMatchReason(reason);
  if (normalized === "-") return "-";

  const pieces: string[] = [];
  if (/brand\+size match/i.test(normalized)) {
    pieces.push(language === "zh" ? "品牌和规格一致" : "brand and size match");
  } else {
    if (/\bbrand\b/i.test(normalized)) pieces.push(language === "zh" ? "品牌一致" : "brand matches");
    if (/\bsize\b/i.test(normalized)) pieces.push(language === "zh" ? "规格一致" : "size matches");
  }

  const overlap = normalized.match(/name overlap\s+([0-9.]+)/i);
  if (overlap) {
    const score = Number.parseFloat(overlap[1]);
    const display = Number.isFinite(score) ? `${Math.round(score * 100)}%` : overlap[1];
    pieces.push(language === "zh" ? `名称重合 ${display}` : `name overlap ${display}`);
  }

  const remainder = normalized
    .replace(/brand\+size match;?/gi, "")
    .replace(/name overlap\s+[0-9.]+;?/gi, "")
    .replace(/;+$/g, "")
    .trim();

  if (remainder && pieces.length === 0) return remainder;
  if (remainder) pieces.push(remainder);
  return pieces.length > 0 ? pieces.join(language === "zh" ? "，" : ", ") : "-";
}

function flattenCategories(categories: CategoryNode[]): CategoryNode[] {
  return categories.flatMap((category) => [category, ...flattenCategories(category.children)]);
}

function countHistoryPoints(history: ProductPriceHistory, selectedStores: StoreOption[]) {
  return filterHistoryStores(history.stores, selectedStores).reduce((sum, store) => sum + store.points.length, 0);
}

function sortStores(stores: StoreOption[]) {
  return [...stores].sort((a, b) => {
    const chainOrder = chainSortOrder(a.supermarket) - chainSortOrder(b.supermarket);
    if (chainOrder !== 0) return chainOrder;
    return a.name.localeCompare(b.name);
  });
}

function chainSortOrder(chain: Supermarket) {
  if (chain === "PaknSave") return 1;
  if (chain === "NewWorld") return 2;
  if (chain === "FreshChoice") return 3;
  return 4;
}

function formatCategoryTrail(category: CategoryNode, departments: CategoryNode[], language: Language) {
  const parent = departments.find((department) => department.id === category.id || department.children.some((child) => child.id === category.id));
  if (!parent || parent.id === category.id) return translateCategoryName(category.name, language);
  return `${translateCategoryName(parent.name, language)} / ${translateCategoryName(category.name, language)}`;
}

function latestHistoryPoint(store: ProductPriceHistory["stores"][number]) {
  return store.points[store.points.length - 1] ?? null;
}

function buildHistoryTrendChart(stores: ProductPriceHistory["stores"], language: Language) {
  const width = 420;
  const height = 210;
  const plotLeft = 46;
  const plotRight = width - 12;
  const plotTop = 16;
  const plotBottom = height - 30;
  const endTime = Date.now();
  const startTime = endTime - 30 * 24 * 60 * 60 * 1000;

  const seriesInput = stores
    .map((store, index) => {
      const points = store.points
        .map((point) => ({ ...point, time: new Date(point.date).getTime() }))
        .filter((point): point is PriceHistoryPoint & { price: number; time: number } => {
          return point.price !== null && Number.isFinite(point.time) && point.time >= startTime && point.time <= endTime;
        })
        .sort((a, b) => a.time - b.time);

      return {
        key: store.store,
        storeName: formatStoreBranch(store.store, store.supermarket as Supermarket, language),
        supermarket: store.supermarket as Supermarket,
        color: historyLineColors[index % historyLineColors.length],
        points
      };
    })
    .filter((series) => series.points.length > 0);

  const allPoints = seriesInput.flatMap((series) => series.points);
  if (allPoints.length === 0) return null;

  const rawMin = Math.min(...allPoints.map((point) => point.price));
  const rawMax = Math.max(...allPoints.map((point) => point.price));
  const padding = Math.max((rawMax - rawMin) * 0.12, 0.25);
  const minPrice = rawMin === rawMax ? rawMin - padding : Math.max(0, rawMin - padding);
  const maxPrice = rawMin === rawMax ? rawMax + padding : rawMax + padding;
  const priceRange = maxPrice - minPrice || 1;

  const toX = (time: number) => plotLeft + ((time - startTime) / (endTime - startTime)) * (plotRight - plotLeft);
  const toY = (price: number) => plotBottom - ((price - minPrice) / priceRange) * (plotBottom - plotTop);

  const series = seriesInput.map((entry) => {
    const points = entry.points.map((point) => ({
      key: point.date,
      x: Number(toX(point.time).toFixed(2)),
      y: Number(toY(point.price).toFixed(2)),
      isOnSpecial: point.isOnSpecial,
      price: point.price,
      date: point.date
    }));

    return {
      key: entry.key,
      storeName: entry.storeName,
      supermarket: entry.supermarket,
      color: entry.color,
      latest: entry.points[entry.points.length - 1] ?? null,
      points,
      path: buildSmoothPath(points)
    };
  });

  const yValues = [maxPrice, (minPrice + maxPrice) / 2, minPrice];
  const yTicks = yValues.map((value) => ({
    label: formatCurrency(value),
    y: Number(toY(value).toFixed(2))
  }));

  return { width, height, plotLeft, plotRight, plotBottom, startTime, yTicks, series };
}

function buildSmoothPath(points: { x: number; y: number }[]) {
  if (points.length === 0) return "";
  if (points.length === 1) return "";
  if (points.length === 2) {
    const [first, second] = points;
    const midX = (first.x + second.x) / 2;
    return `M ${first.x} ${first.y} C ${midX} ${first.y}, ${midX} ${second.y}, ${second.x} ${second.y}`;
  }

  const commands = [`M ${points[0].x} ${points[0].y}`];
  for (let index = 0; index < points.length - 1; index += 1) {
    const previous = points[index - 1] ?? points[index];
    const current = points[index];
    const next = points[index + 1];
    const afterNext = points[index + 2] ?? next;
    const c1x = current.x + (next.x - previous.x) / 6;
    const c1y = current.y + (next.y - previous.y) / 6;
    const c2x = next.x - (afterNext.x - current.x) / 6;
    const c2y = next.y - (afterNext.y - current.y) / 6;
    commands.push(`C ${roundChart(c1x)} ${roundChart(c1y)}, ${roundChart(c2x)} ${roundChart(c2y)}, ${next.x} ${next.y}`);
  }
  return commands.join(" ");
}

function roundChart(value: number) {
  return Number(value.toFixed(2));
}

function formatMonthDay(value: number) {
  return new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" }).format(new Date(value));
}

function hasPriceGap(listings: ProductListing[]) {
  const prices = listings.map((listing) => listing.price).filter((price): price is number => price !== null);
  if (prices.length <= 1) return false;
  return Math.max(...prices) - Math.min(...prices) > 0.009;
}

function describePriceStatus(listings: ProductListing[], t: (typeof copy)[Language]) {
  const prices = listings.map((listing) => listing.price).filter((price): price is number => price !== null);
  if (listings.length === 0 || prices.length === 0) return t.noStoreData;
  if (listings.length <= 1) return t.singleStoreStatus;
  const saving = Math.max(...prices) - Math.min(...prices);
  if (saving <= 0.009) return t.samePriceStatus;
  return `${t.gapStatusPrefix} ${formatCurrency(saving)}`;
}

function describeDealPriceStatus(deal: DealItem, listings: ProductListing[], t: (typeof copy)[Language], language: Language) {
  const pricedListings = listings
    .filter((listing): listing is ProductListing & { price: number } => listing.price !== null)
    .sort((a, b) => a.price - b.price);
  if (pricedListings.length === 0) return t.noStoreData;
  if (pricedListings.length <= 1) return t.singleStoreStatus;

  const lowest = pricedListings[0];
  const highest = pricedListings[pricedListings.length - 1];
  const currentDealListing =
    pricedListings.find((listing) => listing.id === deal.id) ??
    pricedListings.find((listing) => listing.supermarket === deal.supermarket && listing.store === deal.store);
  const gap = highest.price - lowest.price;

  if (currentDealListing && currentDealListing.price <= lowest.price + 0.009) {
    if (gap <= 0.009) return t.samePriceStatus;
    return language === "zh" ? `这条特价也是最低价，最高相差 ${formatCurrency(gap)}` : `This special is also the lowest; top gap ${formatCurrency(gap)}`;
  }

  const lowestStore = `${formatChain(lowest.supermarket)} ${formatStoreBranch(lowest.store, lowest.supermarket, language)}`;
  return language === "zh"
    ? `最低价在 ${lowestStore}：${formatCurrency(lowest.price)}`
    : `Lowest at ${lowestStore}: ${formatCurrency(lowest.price)}`;
}

function formatGroupCaption(group: ProductGroup, language: Language) {
  const parts = [group.description, group.category].filter(Boolean);
  if (parts.length === 0) return language === "zh" ? "未归入同品组" : "Ungrouped listing";
  return parts.join(" · ");
}

function formatListingMeta(listing: ProductListing, group: ProductGroup | null | undefined, language: Language) {
  const category = group?.category ? translateCategoryName(group.category, language) : null;
  const parts = [listing.brand, listing.size, category].filter(Boolean);
  return parts.length > 0 ? parts.join(" · ") : group ? formatGroupCaption(group, language) : "";
}

function formatPriceFreshness(listing: ProductListing, t: (typeof copy)[Language]) {
  return `${t.fresh} ${formatShortDateTime(listing.priceAsOf)}`;
}

function translateCategoryName(name: string, language: Language) {
  if (language !== "zh") return name;
  return categoryNameZh[name] ?? name;
}

function formatReviewCategoryOption(category: CategoryNode, language: Language) {
  const label = translateCategoryName(category.name, language);
  const kind = language === "zh" ? (category.kind === "Aisle" ? "小类" : category.kind === "Shelf" ? "细分类" : "大类") : category.kind;
  return `${label} · ${kind}`;
}

function formatChain(chain: Supermarket) {
  if (chain === "PaknSave") return "PAK'nSAVE";
  if (chain === "NewWorld") return "New World";
  return chain;
}

function formatStoreBranch(store: string, chain: Supermarket, language: Language) {
  if (chain === "Woolworths") return language === "zh" ? "所有门店" : "All stores";
  const chainName = formatChain(chain);
  return store.replace(new RegExp(`^${escapeRegExp(chainName)}\\s*`, "i"), "").trim() || store;
}

function formatFullStoreName(store: string, chain: Supermarket, language: Language) {
  if (chain === "Woolworths") return `${formatChain(chain)} ${formatStoreBranch(store, chain, language)}`;
  return store;
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function formatCurrency(value: number) {
  return new Intl.NumberFormat("en-NZ", {
    style: "currency",
    currency: "NZD",
    minimumFractionDigits: value % 1 === 0 ? 0 : 2
  }).format(value);
}

function formatMaybeCurrency(value: number | null) {
  return value === null ? "-" : formatCurrency(value);
}

function formatDealPrimaryPrice(deal: DealItem) {
  if (deal.price !== null && isVariableWeightDeal(deal)) {
    return formatCurrencyWithUnit(deal.price, deal.unitOfMeasure);
  }
  return formatMaybeCurrency(deal.price);
}

function formatDealWasPrice(deal: DealItem) {
  if (deal.wasPrice === null) return "-";
  return isVariableWeightDeal(deal) ? formatCurrencyWithUnit(deal.wasPrice, deal.unitOfMeasure) : formatCurrency(deal.wasPrice);
}

function formatPromotionWasPrice(wasPrice: number, listing?: ProductListing | null) {
  return listing && isVariableWeightListing(listing) ? formatCurrencyWithUnit(wasPrice, listing.unit) : formatCurrency(wasPrice);
}

function formatUnit(value: number | null, unit: string | null) {
  if (value === null || !unit) return "-";
  return `${formatCurrency(value)} / ${unit}`;
}

function formatCurrencyWithUnit(value: number, unit: string | null | undefined) {
  const suffix = formatReadableUnitSuffix(unit);
  return suffix ? `${formatCurrency(value)} / ${suffix}` : formatCurrency(value);
}

function formatDealSaving(deal: DealItem, language: Language) {
  if (!deal.saving || !deal.wasPrice || deal.wasPrice <= 0) return null;
  const percent = Math.round((deal.saving / deal.wasPrice) * 100);
  const unitSuffix = isVariableWeightDeal(deal) ? formatDealSavingUnitSuffix(deal.unitOfMeasure) : "";
  if (language === "zh") return `省 ${formatCurrency(deal.saving)}${unitSuffix} · ${percent}%`;
  return `Save ${formatCurrency(deal.saving)}${unitSuffix} · ${percent}% off`;
}

function formatDealSavingUnitSuffix(unit: string | null) {
  return formatUnitSuffix(unit);
}

function formatDealDate(value: string, language: Language) {
  const label = isStalePrice(value)
    ? language === "zh"
      ? "数据较旧 · 确认于"
      : "Older data · checked"
    : language === "zh"
      ? "价格确认于"
      : "Price checked";
  return `${label} ${formatShortDateTime(value)}`;
}

function formatDealDateTitle(deal: DealItem, language: Language) {
  const checked = formatDealDate(deal.priceAsOf, language);
  if (!deal.priceUpdatedAt) return checked;
  const changed = language === "zh" ? "观察到变价于" : "Price change observed";
  return `${checked} · ${changed} ${formatShortDateTime(deal.priceUpdatedAt)}`;
}

function isStalePrice(value: string) {
  const checkedAt = new Date(value).getTime();
  if (Number.isNaN(checkedAt)) return false;
  return Date.now() - checkedAt > 36 * 60 * 60 * 1000;
}

function formatScore(value: number) {
  return `${Math.round(value * 100)}%`;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("zh-CN", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}

function latestDate(values: string[]) {
  const validDates = values
    .map((value) => new Date(value))
    .filter((value) => !Number.isNaN(value.getTime()))
    .sort((a, b) => b.getTime() - a.getTime());
  return validDates[0] ?? null;
}

function formatDate(value: Date) {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, "0");
  const day = String(value.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function formatShortDateTime(value: string) {
  const date = new Date(value);
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  const hours = String(date.getHours()).padStart(2, "0");
  const minutes = String(date.getMinutes()).padStart(2, "0");
  return `${year}-${month}-${day} ${hours}:${minutes}`;
}
