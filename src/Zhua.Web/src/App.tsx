import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useState } from "react";
import {
  createItem,
  decideMatchCandidate,
  getAllMatchCandidates,
  getCategories,
  getCategoryProducts,
  getDeals,
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
  ProductGroup,
  ProductListing,
  ProductPriceHistory,
  ProductSort,
  StoreOption,
  Supermarket
} from "./types";

type Language = "zh" | "en";
type ApiSource = "local" | "nas";
type DealCategoryScope = "all" | "selected";

const nasApiBaseUrl = "http://jarvis:8080";
const categoryPageSizeOptions = [10, 20, 30, 50, 100] as const;
const defaultCategoryPageSize = 30;
const dealPageSizeOptions = [10, 20, 24, 50, 100] as const;
const defaultDealPageSize = 10;

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
    cheapest: "当前分类商品",
    compare: "同品各店报价",
    grouping: "同品说明",
    history: "价格历史",
    historyHint: "每个点代表一次真实变价；价格会保持到下一次变价。",
    noRecentChange: "暂无更多变价",
    deals: "今日特价",
    dealsEyebrow: "特价",
    dealSource: "按当前门店筛选",
    dealsDate: "数据日期",
    storeFilter: "门店",
    storeFilterHint: "不选时默认全部门店。分类、排行榜、搜索、详情和特价都会按所选门店展示。",
    storeFilterPending: "当前页面已按所选门店刷新。",
    storeApiMissing: "等待门店 API",
    allStores: "全部门店",
    selectedStores: "已选门店",
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
    candidateQueue: "候选队列",
    productQueue: "商品队列",
    candidateItems: "候选 Item",
    candidateCount: "个候选",
    selectedCandidate: "当前候选",
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
    cheapest: "Products in category",
    compare: "Same-item store prices",
    grouping: "Grouping note",
    history: "Price history",
    historyHint: "Each point is a real price change; the price holds until the next point.",
    noRecentChange: "No extra changes yet",
    deals: "Today's specials",
    dealsEyebrow: "Deals",
    dealSource: "Filtered by selected stores",
    dealsDate: "Data date",
    storeFilter: "Stores",
    storeFilterHint: "No selection means all stores. Categories, rankings, search, detail, and specials are shown for selected stores.",
    storeFilterPending: "Data refreshed for selected stores.",
    storeApiMissing: "Waiting for store API",
    allStores: "All stores",
    selectedStores: "Selected stores",
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
    candidateQueue: "Candidate queue",
    productQueue: "Product queue",
    candidateItems: "Candidate Items",
    candidateCount: "candidates",
    selectedCandidate: "Selected candidate",
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
    pageSize: "Per page",
    previousPage: "Previous",
    nextPage: "Next",
    pageLabel: "Page"
  }
};

const chainClass: Record<Supermarket, string> = {
  Woolworths: "chain-woolworths",
  NewWorld: "chain-newworld",
  PaknSave: "chain-paknsave"
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
  const [categoryRefreshKey, setCategoryRefreshKey] = useState(0);
  const [selectedProductId, setSelectedProductId] = useState("");
  const [comparison, setComparison] = useState<ProductGroup | null>(null);
  const [history, setHistory] = useState<ProductPriceHistory | null>(null);
  const [deals, setDeals] = useState<DealItem[]>([]);
  const [dealPage, setDealPage] = useState(1);
  const [dealPageSize, setDealPageSize] = useState(defaultDealPageSize);
  const [dealTotal, setDealTotal] = useState(0);
  const [dealTotalPages, setDealTotalPages] = useState(1);
  const [dealHasMore, setDealHasMore] = useState(false);
  const [dealRefreshKey, setDealRefreshKey] = useState(0);
  const [dealCategoryScope, setDealCategoryScope] = useState<DealCategoryScope>("selected");
  const [stores, setStores] = useState<StoreOption[]>([]);
  const [selectedStoreIds, setSelectedStoreIds] = useState<string[]>([]);
  const [isReviewMode, setIsReviewMode] = useState(false);
  const [matchCandidates, setMatchCandidates] = useState<MatchCandidate[]>([]);
  const [isReviewLoading, setIsReviewLoading] = useState(false);
  const [reviewActionId, setReviewActionId] = useState<string | null>(null);
  const [reviewError, setReviewError] = useState<string | null>(null);
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
  const t = copy[language];
  const apiBaseUrl = apiSource === "nas" ? nasApiBaseUrl : "";
  const displayedApiUrl = apiSource === "nas" ? nasApiBaseUrl : `${window.location.origin} → localhost:8080`;

  const flattenedCategories = useMemo(() => flattenCategories(categories), [categories]);
  const selectedCategory = flattenedCategories.find((category) => category.id === selectedCategoryId) ?? null;
  const selectedDepartment =
    categories.find((department) => department.id === selectedDepartmentId) ??
    categories.find((department) => department.id === selectedCategoryId || department.children.some((child) => child.id === selectedCategoryId)) ??
    categories[0] ??
    null;
  const storeOptions = useMemo(() => sortStores(stores), [stores]);
  const selectedStores = useMemo(
    () => storeOptions.filter((store) => selectedStoreIds.includes(store.id)),
    [storeOptions, selectedStoreIds]
  );
  const isStoreFilterActive = selectedStoreIds.length > 0;
  const visibleGroups = useMemo(() => prepareProductGroups(groups, selectedStores), [groups, selectedStores]);
  const detailProducts = useMemo(
    () => filterListingsByStores(comparison?.products ?? [], selectedStores),
    [comparison, selectedStores]
  );
  const selectedListing =
    detailProducts.find((product) => product.id === selectedProductId) ??
    detailProducts[0] ??
    findListingById(visibleGroups, selectedProductId) ??
    null;
  const visibleDeals = useMemo(() => sortDeals(deals), [deals]);
  const dealsDataDate = useMemo(() => latestDate(visibleDeals.map((deal) => deal.priceAsOf)), [visibleDeals]);
  const totalProducts = categories.reduce((sum, category) => sum + category.totalProductCount, 0);
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

  function toggleStoreFilter(id: string) {
    setCategoryPage(1);
    setDealPage(1);
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
      await loadMatchCandidates();
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
    if (!selectedCategoryId) return;
    let cancelled = false;

    async function loadDeals() {
      setIsDealsLoading(true);
      try {
        const categoryId = dealCategoryScope === "selected" ? selectedCategoryId : undefined;
        const result = await getDeals(dealPage, dealPageSize, apiBaseUrl, categoryId, selectedStoreIds);
        if (cancelled) return;
        const items = Array.isArray(result.items) ? result.items : [];
        setDeals(items);
        setDealTotal(Number.isFinite(result.total) ? result.total : items.length);
        setDealTotalPages(Math.max(1, result.totalPages));
        setDealHasMore(result.hasMore);
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
  }, [apiBaseUrl, dealCategoryScope, dealPage, dealPageSize, dealRefreshKey, selectedCategoryId, selectedStoreIds, t.failed]);

  useEffect(() => {
    if (!selectedCategoryId) return;
    let cancelled = false;

    async function loadProducts() {
      setIsProductsLoading(true);
      try {
        const result = await getCategoryProducts(selectedCategoryId, categoryPage, categoryPageSize, categorySort, selectedStoreIds, apiBaseUrl);
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
  }, [apiBaseUrl, categoryPage, categoryPageSize, categoryRefreshKey, categorySort, selectedCategoryId, selectedStoreIds, selectedStores, t.failed]);

  function changeCategoryPage(nextPage: number) {
    if (isProductsLoading) return;
    const boundedPage = Math.min(Math.max(1, nextPage), categoryTotalPages);
    if (boundedPage !== categoryPage) setCategoryPage(boundedPage);
  }

  function changeCategorySort(nextSort: ProductSort) {
    setCategorySort(nextSort);
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
    if (boundedPage !== dealPage) setDealPage(boundedPage);
  }

  function changeDealPageSize(nextSize: number) {
    setDealPageSize(nextSize);
    setDealPage(1);
  }

  function refreshDeals() {
    setDealRefreshKey((current) => current + 1);
  }

  function changeDealCategoryScope(nextScope: DealCategoryScope) {
    setDealCategoryScope(nextScope);
    setDealPage(1);
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
          getProductPriceHistory(selectedProductId, 14, apiBaseUrl)
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
    const result = await searchProducts(query, 24, selectedStoreIds, apiBaseUrl, selectedCategoryId || undefined);
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
          <button className={!isReviewMode ? "active" : ""} onClick={() => setIsReviewMode(false)}>
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
          t={t}
          onRefresh={loadMatchCandidates}
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
            pill={`${dealTotal.toLocaleString()} · ${t.dealSource}`}
            actions={
              <div className="scope-toggle" aria-label={language === "zh" ? "特价类别范围" : "Deal category scope"}>
                <button
                  className={dealCategoryScope === "all" ? "active" : ""}
                  onClick={() => changeDealCategoryScope("all")}
                >
                  {language === "zh" ? "全类别" : "All categories"}
                </button>
                <button
                  className={dealCategoryScope === "selected" ? "active" : ""}
                  onClick={() => changeDealCategoryScope("selected")}
                >
                  {language === "zh" ? "所选类别" : "Selected category"}
                </button>
              </div>
            }
            collapsed={collapsedSections.deals}
            expandLabel={t.expand}
            collapseLabel={t.collapse}
            onToggle={() => toggleSection("deals")}
          >
            <div className="table-heading deals-heading">
              <div>
                <h3>{t.deals}</h3>
                <span>
                  {t.pageLabel} {dealPage} / {dealTotalPages} · {t.showingProducts} {visibleDeals.length.toLocaleString()} / {dealTotal.toLocaleString()}
                </span>
              </div>
              <div className="table-controls">
                <button className="refresh-button" onClick={refreshDeals} disabled={isDealsLoading}>
                  {isDealsLoading ? t.loadingShort : t.refresh}
                </button>
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
            <div className="deals-grid">
              {visibleDeals.length === 0 && <p className="empty-state">{t.noDealsForStores}</p>}
              {visibleDeals.map((deal) => (
                <button
                  key={deal.id}
                  className="deal-card"
                  onClick={() => {
                    setSelectedProductId(deal.id);
                    setCollapsedSections((current) => ({ ...current, category: false }));
                  }}
                >
                  <div className="deal-card-top">
                    <ProductImage imageUrl={imageForDeal(deal)} name={deal.product} size="medium" />
                    <div>
                      <ChainBadge chain={deal.supermarket} />
                      <h3>{deal.product}</h3>
                      <p>{deal.brand || "-"}</p>
                    </div>
                  </div>
                  <div className="deal-price">
                    <strong>{formatMaybeCurrency(deal.price)}</strong>
                    {formatDealSaving(deal, language) && <em className="deal-saving">{formatDealSaving(deal, language)}</em>}
                    {deal.wasPrice !== null && (
                      <span>
                        {t.was} {formatCurrency(deal.wasPrice)}
                      </span>
                    )}
                  </div>
                  <footer>
                    <span>{formatStoreBranch(deal.store, deal.supermarket, language)}</span>
                    <span>{formatUnit(deal.unitPrice, deal.unitOfMeasure)}</span>
                  </footer>
                </button>
              ))}
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
              pill={`${searchTotal.toLocaleString()} ${t.productCount}`}
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
            title={selectedCategory ? translateCategoryName(selectedCategory.name, language) : t.cheapest}
            pill={`${categoryTotal.toLocaleString()} ${t.productCount}`}
            collapsed={collapsedSections.category}
            expandLabel={t.expand}
            collapseLabel={t.collapse}
            onToggle={() => toggleSection("category")}
          >

            <div className="department-tabs" aria-label={t.departmentMetric}>
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
                      {t.pageLabel} {categoryPage} / {categoryTotalPages} · {t.showingProducts} {visibleGroups.length.toLocaleString()} / {categoryTotal.toLocaleString()}
                    </span>
                  </div>
                  <div className="table-controls">
                    <button className="refresh-button" onClick={refreshCategoryProducts} disabled={isProductsLoading}>
                      {isProductsLoading ? t.loadingShort : t.refresh}
                    </button>
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
                        return (
                          <tr
                            key={listing.id}
                            className={selectedProductId === listing.id ? "selected-row" : ""}
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
                            <td>{listing.brand || "-"}</td>
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
                            <td>{formatUnit(listing.unitPrice, listing.unit)}</td>
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

              <aside className="detail-card">
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
                        <div className="history-list">
                          {filterHistoryStores(history.stores, selectedStores).map((store) => (
                            <article key={store.store}>
                              <div>
                                <ChainBadge chain={store.supermarket as Supermarket} />
                                <strong>{formatStoreBranch(store.store, store.supermarket as Supermarket, language)}</strong>
                              </div>
                              <div className="history-current">
                                <span className="history-current-price">
                                  <strong>{formatMaybeCurrency(latestHistoryPoint(store)?.price ?? null)}</strong>
                                  <PromotionBadges point={latestHistoryPoint(store)} language={language} compact />
                                </span>
                                <span>{describeHistory(store, t)}</span>
                              </div>
                              <div className="history-points" aria-label={t.history}>
                                {store.points.slice(-3).map((point) => (
                                  <span className="history-point" key={`${store.store}-${point.date}`} title={formatDateTime(point.date)}>
                                    <strong>{formatMaybeCurrency(point.price)}</strong>
                                    <PromotionBadges point={point} language={language} compact />
                                  </span>
                                ))}
                              </div>
                            </article>
                          ))}
                        </div>
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
    <button className="search-result" onClick={onSelect}>
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

function ReviewWorkspaceV2({
  candidates,
  categories,
  isLoading,
  actionId,
  error,
  language,
  apiBaseUrl,
  t,
  onRefresh,
  onAction
}: {
  candidates: MatchCandidate[];
  categories: CategoryNode[];
  isLoading: boolean;
  actionId: string | null;
  error: string | null;
  language: Language;
  apiBaseUrl: string;
  t: (typeof copy)[Language];
  onRefresh: () => void | Promise<void>;
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
  const [isCreateItemOpen, setIsCreateItemOpen] = useState(false);
  const [isCreatingItem, setIsCreatingItem] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const productGroups = useMemo(() => groupMatchCandidates(candidates), [candidates]);
  const selectedProductGroup = productGroups.find((group) => group.productId === selectedProductId) ?? productGroups[0] ?? null;
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

      {error && <p className="empty-state error-card">{error}</p>}
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
                  <span>{sourceListing?.brand ?? selectedCandidate.brand ?? "-"}</span>
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
                      <small>{group.brand || "-"}{group.size ? ` · ${group.size}` : ""}</small>
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
                {selectedProductGroup.candidates.map((candidate) => (
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
                    {candidate.id === selectedCandidate?.id && (
                      <p className="review-meta candidate-item-meta">
                        <span>{destinationGroup?.category ?? t.category}</span>
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
                ))}
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
  if (supermarket === "Woolworths" || !sku) return null;
  const productId = sku.match(/^\d+/)?.[0];
  return productId ? `https://a.fsimg.co.nz/product/retail/fan/image/400x400/${productId}.png` : null;
}

function ChainBadge({ chain }: { chain: Supermarket }) {
  return <span className={`chain-badge ${chainClass[chain]}`}>{formatChain(chain)}</span>;
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
          {wasPrice !== null ? ` · ${labels.was} ${formatCurrency(wasPrice)}` : ""}
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

function prepareProductGroups(groups: ProductGroup[], selectedStores: StoreOption[]) {
  return groups
    .map((group) => ({
      ...group,
      products: filterListingsByStores(group.products, selectedStores)
    }))
    .filter((group) => group.products.length > 0);
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

function sortDeals(deals: DealItem[]) {
  return deals
    .filter((deal) => deal.price !== null)
    .sort((a, b) => (b.saving ?? 0) - (a.saving ?? 0));
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
  return 3;
}

function formatCategoryTrail(category: CategoryNode, departments: CategoryNode[], language: Language) {
  const parent = departments.find((department) => department.id === category.id || department.children.some((child) => child.id === category.id));
  if (!parent || parent.id === category.id) return translateCategoryName(category.name, language);
  return `${translateCategoryName(parent.name, language)} / ${translateCategoryName(category.name, language)}`;
}

function latestHistoryPoint(store: ProductPriceHistory["stores"][number]) {
  return store.points[store.points.length - 1] ?? null;
}

function describeHistory(store: ProductPriceHistory["stores"][number], t: (typeof copy)[Language]) {
  if (store.points.length <= 1) return t.noRecentChange;
  const latest = latestHistoryPoint(store);
  return latest ? `${t.fresh} ${formatShortDateTime(latest.date)}` : t.noRecentChange;
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

function formatUnit(value: number | null, unit: string | null) {
  if (value === null || !unit) return "-";
  return `${formatCurrency(value)} / ${unit}`;
}

function formatDealSaving(deal: DealItem, language: Language) {
  if (!deal.saving || !deal.wasPrice || deal.wasPrice <= 0) return null;
  const percent = Math.round((deal.saving / deal.wasPrice) * 100);
  if (language === "zh") return `省 ${formatCurrency(deal.saving)} · ${percent}%`;
  return `Save ${formatCurrency(deal.saving)} · ${percent}% off`;
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
