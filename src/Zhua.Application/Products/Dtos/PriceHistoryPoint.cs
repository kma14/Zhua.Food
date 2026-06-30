namespace Zhua.Application.Products;

/// <summary>One observed price change for a store product (a <c>PriceSnapshot</c>, change-only — D3).</summary>
public sealed record PriceHistoryPoint(
    DateTimeOffset Date,       // CapturedAt — when this price took effect
    decimal? Price,
    bool IsOnSpecial,
    decimal? WasPrice,         // NonSpecialPrice at the time (null for Foodstuffs — not published)
    decimal? UnitPrice);
