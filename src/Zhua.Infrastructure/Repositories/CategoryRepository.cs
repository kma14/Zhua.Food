using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;
using Zhua.Domain.Repositories;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="ICategoryRepository"/> — pure data access; the service builds the tree.</summary>
public sealed class CategoryRepository(ZhuaDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetActiveAsync(CancellationToken ct = default) =>
        await db.Categories.AsNoTracking().Where(c => !c.IsArchived).ToListAsync(ct);

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default) =>
        await db.Categories.ToListAsync(ct);   // tracked — the cascade-archive walk mutates these

    public Task<Category?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountGroupsByCategoryAsync(
        IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var hasStoreFilter = storeIds is { Count: > 0 };

        // Matched items via Item.CategoryId (a group per item, as before).
        var itemCounts = await db.Items
            .Where(p => p.CategoryId != null
                && (!hasStoreFilter || p.Products.Any(sp => sp.CurrentPrice != null && storeIds!.Contains(sp.StoreId))))
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Unmatched listings via their own store-category → shared category (TD-5) — the same rows browse now shows.
        // Distinct on (category, product) so a listing under several shelves that map to one node counts once there.
        var unmatchedCounts = await db.Products
            .Where(p => p.ItemId == null && p.CurrentPrice != null && p.Store.IsActive && p.IsAvailable
                && (!hasStoreFilter || storeIds!.Contains(p.StoreId)))
            .SelectMany(p => p.Categories
                .Where(sc => sc.CategoryId != null)
                .Select(sc => new { CatId = sc.CategoryId!.Value, ProductId = p.Id }))
            .Distinct()
            .GroupBy(x => x.CatId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totals = itemCounts.ToDictionary(x => x.Id, x => x.Count);
        foreach (var u in unmatchedCounts)
            totals[u.Id] = totals.GetValueOrDefault(u.Id) + u.Count;
        return totals;
    }

    public Task<bool> PathExistsAsync(string path, CancellationToken ct = default) =>
        db.Categories.AnyAsync(c => c.Path == path, ct);

    public void Add(Category category) => db.Categories.Add(category);
}
