namespace Zhua.Application.Deals;

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
