using Zhua.Application.Common;
using Zhua.Domain.Enums;

namespace Zhua.Application.Deals;

/// <summary>Current specials as a paged envelope, filterable by supermarket/category/store (aligned with /products).</summary>
public interface IDealQueries
{
    /// <returns><c>null</c> only when <paramref name="categoryId"/> is given but unknown/archived (→ 404).</returns>
    Task<PagedResult<DealItem>?> ListAsync(
        Chain? supermarket, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size);
}
