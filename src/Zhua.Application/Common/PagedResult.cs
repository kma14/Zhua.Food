namespace Zhua.Application.Common;

/// <summary>
/// A server-paged, server-sorted slice of a collection. Sorting is applied over the whole filtered set
/// <b>before</b> paging, so <paramref name="Items"/> is a globally-correct page. <paramref name="Total"/> is the
/// count of items after all filters (for products: the number of groups after the storeId filter), not raw rows.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int Size,
    int Total,
    int TotalPages,
    bool HasMore,
    string Sort);
