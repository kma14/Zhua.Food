namespace Zhua.Application.Products;

/// <summary>A product's price history across stores (one series per store).</summary>
public sealed record ProductPriceHistory(
    Guid Id,
    string Name,
    string? Brand,
    string? Size,
    IReadOnlyList<StorePriceHistory> Stores);
