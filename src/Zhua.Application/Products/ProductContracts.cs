namespace Zhua.Application.Products;

/// <summary>
/// A group of store listings we think are the same product (D25), plus the item metadata. The <c>Products</c> list
/// is the payload — every per-store listing as-is; the API computes <b>no</b> cheapest/saving/count, so the client
/// ranks them however it likes (cheapest, nearest, on-special). Root carries only item metadata (the item itself is
/// internal: just <c>itemId</c> + <c>description</c>). An unmatched listing is a group of one (<c>itemId: null</c>).
/// </summary>
public sealed record ProductGroup(
    Guid? ItemId,                 // internal grouping id; null = an unmatched listing (a group of one)
    string? Description,          // item grouping caption ("we think these are: X", D25); client decides usage
    string? Category,             // item category leaf name (denormalized); null if unmatched
    IReadOnlyList<ProductListing> Products);

/// <summary>One store's listing of a product — pure per-listing facts (no group aggregates).</summary>
public sealed record ProductListing(
    Guid Id,                 // this listing's product id — drill in via GET /products/{id}
    string Store,            // the store's display name, e.g. "PAK'nSAVE Albany"
    string Supermarket,      // Woolworths | NewWorld | PaknSave (internally Domain enum Chain)
    string Suburb,
    string Name,             // the store's own listing name
    string? Brand,
    string? Size,
    string? ImageUrl,
    decimal? Price,
    bool IsOnSpecial,
    decimal? WasPrice,       // regular price when on special (Woolworths published / Foodstuffs reconstructed, D23)
    decimal? UnitPrice,      // normalised COMPARABLE unit price (per kg/L/ea) — server-normalised; null if N/A
    string? Unit,            // "1kg" | "1L" | "1ea"
    DateTimeOffset? PriceUpdatedAt,   // when this store's price last changed (D3)
    DateTimeOffset PriceAsOf);        // when it was last confirmed in a crawl (LastSeenAt)

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

/// <summary>
/// Set a product's item link — PATCH /products/{id}. An item id links it (the reviewer's manual override when no
/// candidate fits); <c>null</c> clears it (unlink).
/// </summary>
public sealed record UpdateProductLinkRequest(Guid? ItemId);

/// <summary>A product's item link after a PATCH.</summary>
public sealed record ProductLinkView(Guid Id, Guid? ItemId);
