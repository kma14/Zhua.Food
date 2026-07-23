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
    // True when the ITEM is carried by more than one store in the whole catalogue — i.e. a real cross-store
    // comparison exists. **Global, independent of any ?storeId= filter**: a store-filtered response may return a
    // single listing for a group yet still be Comparable, because the item has prices at other stores too. An
    // unmatched listing (ItemId null) is never comparable. Lets the client render a compare card vs. a single-store
    // card without leaking the internal item; the service sets it (Products.Count when unfiltered, the item's global
    // store span when a store filter narrowed the listings).
    bool Comparable,
    IReadOnlyList<ProductListing> Products);
