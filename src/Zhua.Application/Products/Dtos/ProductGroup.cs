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
