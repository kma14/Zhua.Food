using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="IStoreRepository"/> — pure data access; the Application service projects.</summary>
public sealed class StoreRepository(ZhuaDbContext db) : IStoreRepository
{
    public async Task<IReadOnlyList<Store>> ListActiveAsync(Chain? supermarket, CancellationToken ct = default) =>
        await db.Stores
            .Where(s => s.IsActive && (supermarket == null || s.Chain == supermarket))
            .OrderBy(s => s.Chain).ThenBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountPricedProductsAsync(CancellationToken ct = default) =>
        await db.Products
            .Where(p => p.CurrentPrice != null)
            .GroupBy(p => p.StoreId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastSucceededCrawlAsync(CancellationToken ct = default) =>
        await db.CrawlRuns
            .Where(r => r.Status == CrawlRunStatus.Succeeded && r.FinishedAt != null)
            .GroupBy(r => r.StoreId)
            .Select(g => new { g.Key, Last = g.Max(r => r.FinishedAt!.Value) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);
}
