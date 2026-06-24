namespace Zhua.Api.Contracts;

/// <summary>A canonical product in search results, with its cheapest price across stores.</summary>
public sealed record ProductSummary(
    Guid Id,
    string Name,
    string? Brand,
    string? Size,
    string Category,
    string? ImageUrl,             // cheapest store's product image (Foodstuffs may resolve to a CDN placeholder)
    decimal? CheapestPrice,
    int StoreCount,
    bool OnSpecialSomewhere,
    DateTimeOffset? PriceAsOf);   // cheapest store's LastSeenAt — when that price was last confirmed

/// <summary>What one store charges for a canonical product (keeps the store's own name).</summary>
public sealed record StorePrice(
    string Store,
    string Supermarket,      // Woolworths | NewWorld | PaknSave (internally Domain enum Chain)
    string Suburb,
    string StoreName,        // the store's own RawName for this product
    string? ImageUrl,        // this store's product image
    decimal? Price,
    bool IsOnSpecial,
    decimal? NonSpecialPrice,
    decimal? UnitPrice,
    string? UnitOfMeasure,
    DateTimeOffset? PriceUpdatedAt,   // when this store's price last changed (D3)
    DateTimeOffset PriceAsOf);        // when it was last confirmed in a crawl (LastSeenAt)

/// <summary>Same-product cross-store comparison (the core "where's it cheapest" view).</summary>
public sealed record ProductComparison(
    Guid Id,
    string Name,
    string? Brand,
    string? Size,
    string Category,
    string? ImageUrl,        // representative image (the cheapest store's)
    decimal? CheapestPrice,
    decimal? Saving,         // dearest − cheapest across stores
    IReadOnlyList<StorePrice> Prices);

/// <summary>One product inside a category (merged across stores), shown at its cheapest store.</summary>
public sealed record CategoryProduct(
    Guid Id,
    string Product,            // canonical/display name
    string? Brand,
    string? Size,
    string? ImageUrl,          // the cheapest store's product image
    string? OriginalName,      // the cheapest store's own raw name
    decimal? CheapestPrice,    // lowest shelf price across its stores
    decimal? UnitPrice,        // normalised comparable unit price (per kg/L/ea); null if not comparable
    string? Unit,              // "1kg" | "1L" | "1ea"
    int StoreCount,
    string CheapestStore,
    string Supermarket,
    bool OnSpecialSomewhere,
    DateTimeOffset? PriceUpdatedAt,   // cheapest store's: when its price last changed
    DateTimeOffset PriceAsOf);        // cheapest store's: when last confirmed in a crawl

/// <summary>A product currently on special at a store.</summary>
public sealed record DealItem(
    string Product,
    string? Brand,
    string? ImageUrl,        // this store's product image
    string Store,
    string Supermarket,
    decimal? Price,
    decimal? WasPrice,
    decimal? Saving,
    decimal? UnitPrice,
    string? UnitOfMeasure,
    DateTimeOffset? PriceUpdatedAt,   // when this special's price last changed
    DateTimeOffset PriceAsOf);        // when last confirmed in a crawl

/// <summary>One observed price change for a store product (a <c>PriceSnapshot</c>, change-only — D3).</summary>
public sealed record PriceHistoryPoint(
    DateTimeOffset Date,       // CapturedAt — when this price took effect
    decimal? Price,
    bool IsOnSpecial,
    decimal? WasPrice,         // NonSpecialPrice at the time (null for Foodstuffs — not published)
    decimal? UnitPrice);

/// <summary>One store's price history for a product — a step series (price holds until the next point).</summary>
public sealed record StorePriceHistory(
    string Store,
    string Supermarket,
    string Suburb,
    IReadOnlyList<PriceHistoryPoint> Points);

/// <summary>A product's price history across stores (one series per store).</summary>
public sealed record ProductPriceHistory(
    Guid Id,
    string Name,
    string? Brand,
    string? Size,
    IReadOnlyList<StorePriceHistory> Stores);

/// <summary>A node in the shared canonical category tree (D22) — Department → Aisle → Shelf.</summary>
public sealed record CategoryNode(
    Guid Id,
    string Kind,              // Department | Aisle | Shelf
    string Name,
    string Slug,
    string Path,             // full slug path, e.g. "meat-poultry-seafood/beef"
    int ProductCount,        // canonical products directly on this node
    int TotalProductCount,   // including all descendants (useful at Department/Aisle level)
    IReadOnlyList<CategoryNode> Children);

/// <summary>A physical store the app tracks prices for (active stores only).</summary>
public sealed record StoreView(
    Guid Id,
    string Supermarket,      // Woolworths | NewWorld | PaknSave
    string Name,             // the store's display name, e.g. "PAK'nSAVE Albany"
    string Suburb,
    double Latitude,
    double Longitude,
    int ProductCount,                  // priced listings we currently hold for this store
    DateTimeOffset? LastCrawledAt);    // when this store last finished a successful crawl (freshness)

/// <summary>A pending cross-store match awaiting review (the queue).</summary>
public sealed record MatchCandidateView(
    Guid Id,
    string StoreProductName,
    string? Brand,
    string? Size,
    string Supermarket,
    decimal? Price,
    string CandidateCanonical,
    double Score,
    string? Reason);
