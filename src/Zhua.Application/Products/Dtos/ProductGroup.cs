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
    IReadOnlyList<ProductListing> Products)
{
    /// <summary>
    /// True when this group offers a real cross-store comparison — i.e. more than one store's listing. A single
    /// store's listing is <c>false</c>, whether it's unmatched (<c>ItemId</c> null) or a product only one store
    /// carries. Lets the client render a compare card vs. a "single store — no cross-store price yet" card without
    /// re-deriving it or leaking the internal item. Computed from <see cref="Products"/>, never stored.
    /// </summary>
    public bool Comparable => Products.Count > 1;
}
