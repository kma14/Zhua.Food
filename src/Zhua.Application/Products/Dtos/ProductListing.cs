namespace Zhua.Application.Products;

/// <summary>One store's listing of a product — pure per-listing facts (no group aggregates).</summary>
public sealed record ProductListing(
    Guid Id,                 // this listing's product id — drill in via GET /products/{id}
    string Sku,        // source-store SKU/product id from Woolworths/Foodstuffs; useful for admin review
    string Store,            // the store's display name, e.g. "PAK'nSAVE Albany"
    string Supermarket,      // Woolworths | NewWorld | PaknSave (internally Domain enum Chain)
    string Suburb,
    string Name,             // the store's own listing name
    string? Brand,
    string? Size,
    string? ImageUrl,
    decimal? Price,          // always the unit price a CARDLESS shopper pays now (promotions-model.md)
    bool IsOnSpecial,        // promoType == "Special" (public special) — member/multibuy do NOT count
    decimal? WasPrice,       // regular price when on special (Woolworths published / Foodstuffs reconstructed, D23)
    string? PromoType,       // "Special" | "MemberPrice" | "Multibuy" | null (no promotion)
    decimal? MemberPrice,    // loyalty-card price when PromoType == "MemberPrice"
    int? MultibuyQuantity,   // "N for $X": N — set whenever the source publishes a multibuy
    decimal? MultibuyTotal,  // "N for $X": the total $X (not a unit price)
    decimal? UnitPrice,      // normalised COMPARABLE unit price (per kg/L/ea) — server-normalised; null if N/A
    string? Unit,            // "1kg" | "1L" | "1ea"
    DateTimeOffset? PriceUpdatedAt,   // when this store's price last changed (D3)
    DateTimeOffset PriceAsOf);        // when it was last confirmed in a crawl (LastSeenAt)
