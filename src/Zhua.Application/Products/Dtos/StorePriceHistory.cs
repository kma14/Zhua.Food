namespace Zhua.Application.Products;

/// <summary>One store's price history for a product — a step series (price holds until the next point).</summary>
public sealed record StorePriceHistory(
    string Store,
    string Supermarket,
    string Suburb,
    IReadOnlyList<PriceHistoryPoint> Points);
