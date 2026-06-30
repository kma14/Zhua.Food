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

    public async Task<IReadOnlyDictionary<Guid, int>> CountItemsByCategoryAsync(
        IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var hasStoreFilter = storeIds is { Count: > 0 };
        return await db.Items
            .Where(p => p.CategoryId != null
                && (!hasStoreFilter || p.Products.Any(sp => sp.CurrentPrice != null && storeIds!.Contains(sp.StoreId))))
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
    }

    public Task<bool> PathExistsAsync(string path, CancellationToken ct = default) =>
        db.Categories.AnyAsync(c => c.Path == path, ct);

    public void Add(Category category) => db.Categories.Add(category);
}
