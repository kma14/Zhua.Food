using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>EF implementation of <see cref="IDealQueries"/> (D27) — current specials, biggest saving first.</summary>
public sealed class DealQueries(ZhuaDbContext db) : IDealQueries
{
    public async Task<IReadOnlyList<DealItem>> ListAsync(Chain? supermarket, int page, int size)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);

        return await db.Products
            .Where(sp => sp.Store.IsActive
                && sp.IsOnSpecial
                && sp.CurrentNonSpecialPrice != null
                && sp.CurrentPrice != null
                && (supermarket == null || sp.Store.Chain == supermarket))
            .OrderByDescending(sp => sp.CurrentNonSpecialPrice - sp.CurrentPrice)
            .Skip((page - 1) * size).Take(size)
            .Select(sp => new DealItem(
                sp.RawName, sp.RawBrand, sp.ImageUrl, sp.Store.Name, sp.Store.Chain.ToString(),
                sp.CurrentPrice, sp.CurrentNonSpecialPrice, sp.CurrentNonSpecialPrice - sp.CurrentPrice,
                sp.UnitPrice, sp.UnitOfMeasure, sp.PriceUpdatedAt, sp.LastSeenAt))
            .ToListAsync();
    }
}
