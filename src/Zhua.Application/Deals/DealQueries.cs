using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Deals;

/// <summary>
/// Current specials, biggest saving first (D27). Loads the on-special listings via <see cref="IProductRepository"/>
/// and shapes the <see cref="DealItem"/> DTO (incl. the saving) — no EF here.
/// </summary>
public sealed class DealQueries(IProductRepository products) : IDealQueries
{
    public async Task<IReadOnlyList<DealItem>> ListAsync(Chain? supermarket, int page, int size)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);

        var specials = await products.FindSpecialsAsync(supermarket, page, size);
        return specials.Select(p => new DealItem(
                p.RawName, p.RawBrand, p.ImageUrl, p.Store.Name, p.Store.Chain.ToString(),
                p.CurrentPrice, p.CurrentNonSpecialPrice, p.CurrentNonSpecialPrice - p.CurrentPrice,
                p.UnitPrice, p.UnitOfMeasure, p.PriceUpdatedAt, p.LastSeenAt))
            .ToList();
    }
}
