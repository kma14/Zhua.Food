namespace Zhua.Application.Products;

/// <summary>One observed price change for a store product (a <c>PriceSnapshot</c>, change-only — D3).</summary>
public sealed record PriceHistoryPoint(
    DateTimeOffset Date,       // CapturedAt — when this price took effect
    decimal? Price,
    bool IsOnSpecial,          // promoType == "Special" at the time
    decimal? WasPrice,         // NonSpecialPrice at the time (source-published or D23-reconstructed)
    string? PromoType,         // "Special" | "MemberPrice" | "Multibuy" | null — promo-type flips are history (B2)
    decimal? MemberPrice,      // loyalty-card price at the time
    decimal? UnitPrice);
