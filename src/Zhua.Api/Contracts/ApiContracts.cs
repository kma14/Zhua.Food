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
    string Chain,
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

/// <summary>A product currently on special at a store.</summary>
public sealed record DealItem(
    string Product,
    string? Brand,
    string Store,
    string Chain,
    decimal? Price,
    decimal? WasPrice,
    decimal? Saving,
    decimal? UnitPrice,
    string? UnitOfMeasure);

/// <summary>A pending cross-store match awaiting review (the queue).</summary>
public sealed record MatchCandidateView(
    Guid Id,
    string StoreProductName,
    string? Brand,
    string? Size,
    string Chain,
    decimal? Price,
    string CandidateCanonical,
    double Score,
    string? Reason);
