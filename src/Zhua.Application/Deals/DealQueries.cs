using Zhua.Application.Categories;
using Zhua.Application.Common;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Deals;

/// <summary>
/// Current specials as a paged envelope (D27), filterable by supermarket/category/store — the category subtree +
/// store filters go through the same logic as /products (shared <see cref="CategorySubtree"/> + repository query), so
/// deals filter identically. Loads the on-special listings via <see cref="IProductRepository"/> and shapes the
/// <see cref="DealItem"/> DTO (incl. the saving) — no EF here.
/// </summary>
public sealed class DealQueries(IProductRepository products, ICategoryRepository categories) : IDealQueries
{
    public async Task<PagedResult<DealItem>?> ListAsync(
        Chain? supermarket, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);

        // Same category resolution as /products: an unknown/archived id → null → 404.
        IReadOnlyCollection<Guid>? subtree = null;
        if (categoryId is { } catId)
        {
            subtree = CategorySubtree.Resolve(await categories.GetActiveAsync(), catId);
            if (subtree is null) return null;
        }

        var total = await products.CountSpecialsAsync(supermarket, subtree, storeIds);
        var specials = await products.FindSpecialsAsync(supermarket, subtree, storeIds, page, size);
        var items = specials.Select(p => new DealItem(
                p.Id, p.Sku,
                p.RawName, p.RawBrand, p.ImageUrl, p.Store.Name, p.Store.Chain.ToString(),
                p.CurrentPrice, p.CurrentNonSpecialPrice, p.CurrentNonSpecialPrice - p.CurrentPrice,
                p.UnitPrice, p.UnitOfMeasure, p.PriceUpdatedAt, p.LastSeenAt))
            .ToList();

        var totalPages = (int)Math.Ceiling(total / (double)size);
        return new PagedResult<DealItem>(items, page, size, total, totalPages, page < totalPages);
    }
}
