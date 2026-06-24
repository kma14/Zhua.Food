namespace Zhua.Api.Contracts;

/// <summary>A canonical product in search results, with its cheapest price across stores.</summary>
public sealed record ProductSummary(
    Guid Id,
    string Name,
    string? Brand,
    string? Size,
    string Category,
    decimal? CheapestPrice,
    int StoreCount,
    bool OnSpecialSomewhere);

/// <summary>What one store charges for a canonical product (keeps the store's own name).</summary>
public sealed record StorePrice(
    string Store,
    string Supermarket,      // Woolworths | NewWorld | PaknSave (internally Domain enum Chain)
    string Suburb,
    string StoreName,        // the store's own RawName for this product
    decimal? Price,
    bool IsOnSpecial,
    decimal? NonSpecialPrice,
    decimal? UnitPrice,
    string? UnitOfMeasure,
    DateTimeOffset LastSeenAt);

/// <summary>Same-product cross-store comparison (the core "where's it cheapest" view).</summary>
public sealed record ProductComparison(
    Guid Id,
    string Name,
    string? Brand,
    string? Size,
    string Category,
    decimal? CheapestPrice,
    decimal? Saving,         // dearest − cheapest across stores
    IReadOnlyList<StorePrice> Prices);

/// <summary>One product inside a category (merged across stores), shown at its cheapest store.</summary>
public sealed record CategoryProduct(
    Guid Id,
    string Product,            // canonical/display name
    string? Brand,
    string? Size,
    string? OriginalName,      // the cheapest store's own raw name
    decimal? CheapestPrice,    // lowest shelf price across its stores
    decimal? UnitPrice,        // normalised comparable unit price (per kg/L/ea); null if not comparable
    string? Unit,              // "1kg" | "1L" | "1ea"
    int StoreCount,
    string CheapestStore,
    string Supermarket,
    bool OnSpecialSomewhere);

/// <summary>A product currently on special at a store.</summary>
public sealed record DealItem(
    string Product,
    string? Brand,
    string Store,
    string Supermarket,
    decimal? Price,
    decimal? WasPrice,
    decimal? Saving,
    decimal? UnitPrice,
    string? UnitOfMeasure);

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
