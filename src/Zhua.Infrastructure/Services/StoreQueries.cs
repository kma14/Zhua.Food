using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>EF implementation of <see cref="IStoreQueries"/> (D27) — the active stores we track.</summary>
public sealed class StoreQueries(ZhuaDbContext db) : IStoreQueries
{
    public async Task<IReadOnlyList<StoreView>> ListAsync(Chain? supermarket) =>
        await db.Stores
            .Where(s => s.IsActive && (supermarket == null || s.Chain == supermarket))
            .OrderBy(s => s.Chain).ThenBy(s => s.Name)
            .Select(s => new StoreView(
                s.Id, s.Chain.ToString(), s.Name, s.Suburb, s.Latitude, s.Longitude,
                s.Products.Count(sp => sp.CurrentPrice != null),
                s.CrawlRuns.Where(r => r.Status == CrawlRunStatus.Succeeded)
                    .Max(r => (DateTimeOffset?)r.FinishedAt)))
            .ToListAsync();
}
