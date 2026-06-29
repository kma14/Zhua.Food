namespace Zhua.Api.Contracts;

/// <summary>
/// A group of store listings we think are the same product (D25), plus the item metadata. The <c>Products</c> list
/// is the payload — every per-store listing as-is; the API computes **no** cheapest/saving/count, so the client
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

/// <summary>A node in the shared category tree (D22) — Department → Aisle → Shelf.</summary>
public sealed record CategoryNode(
    Guid Id,
    string Kind,              // Department | Aisle | Shelf
    string Name,
    string Slug,
    string Path,             // full slug path, e.g. "meat-poultry-seafood/beef"
    int ProductCount,        // items directly on this node
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
    Guid ProductId,             // the listing under review — target of PATCH /products/{id}
    string ProductName,
    string? Brand,
    string? Size,
    string Supermarket,
    decimal? Price,
    Guid CandidateItemId,       // the proposed item's id (approve uses it, or pre-fill a manual link)
    string CandidateItem,
    double Score,
    string? Reason);

/// <summary>Create a curated category (plan D25 phase 3). Kind = Department | Aisle | Shelf.</summary>
public sealed record CreateCategoryRequest(string Kind, string Name, Guid? ParentId);

/// <summary>Rename a category's display name (plan D25 phase 3) — its path/slug stay as the stable key.</summary>
public sealed record RenameCategoryRequest(string Name);

/// <summary>A category as returned by the admin create/rename actions.</summary>
public sealed record CategorySummary(Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId);

/// <summary>
/// Set a store product's item link — PATCH /store-products/{id}. A item id links it (the reviewer's manual
/// override when no candidate fits); <c>null</c> clears it (unlink).
/// </summary>
public sealed record UpdateProductLinkRequest(Guid? ItemId);

/// <summary>A store product's item link after a PATCH.</summary>
public sealed record ProductLinkView(Guid Id, Guid? ItemId);

/// <summary>
/// Create a item (internal join key, plan D25) — POST /items. <c>Name</c> is the grouping anchor;
/// <c>Description</c> defaults to it. The review UI pre-fills these from the listing it's creating the item for,
/// then links that listing via PATCH /store-products/{id}.
/// </summary>
public sealed record CreateItemRequest(string Name, string? Description, string? Brand, string? Size, string? Category);

/// <summary>A item (internal — never a shopper label) as returned by the admin create action.</summary>
public sealed record ItemView(Guid Id, string Name, string? Description, string? Brand, string? Size, string Category);

/// <summary>Decide a pending match candidate — PATCH /match-candidates/{id}. Status = <c>approved</c> | <c>rejected</c>.</summary>
public sealed record UpdateMatchCandidateRequest(string Status);

/// <summary>A match candidate's state after a decision (ItemId is set only when approved).</summary>
public sealed record MatchCandidateDecision(Guid Id, string Status, Guid? ItemId);
