using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="IMatchingRepository"/> — bulk loads for the offline matcher + mapper.</summary>
public sealed class MatchingRepository(ZhuaDbContext db) : IMatchingRepository
{
    // --- ItemMatcher ---
    public async Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default) =>
        await db.Products
            .Include(p => p.Store)
            .Include(p => p.Categories)
            .Where(p => p.Store.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Item>> GetAllItemsAsync(CancellationToken ct = default) =>
        await db.Items.ToListAsync(ct);

    public async Task<IReadOnlyList<MatchCandidate>> GetAllCandidatesAsync(CancellationToken ct = default) =>
        await db.MatchCandidates.ToListAsync(ct);

    public void AddItem(Item item) => db.Items.Add(item);
    public void AddCandidate(MatchCandidate candidate) => db.MatchCandidates.Add(candidate);
    public void RemoveCandidates(IEnumerable<MatchCandidate> candidates) => db.MatchCandidates.RemoveRange(candidates);

    public async Task<IReadOnlyList<MatchCandidate>> GetResolvedPendingCandidatesAsync(CancellationToken ct = default) =>
        await db.MatchCandidates
            .Where(m => m.Status == MatchStatus.Pending && m.Product.ItemId != null)
            .ToListAsync(ct);

    public Task<int> CountActiveItemsAsync(CancellationToken ct = default) =>
        db.Items.CountAsync(c => c.MergedIntoId == null, ct);

    public Task<int> CountPendingCandidatesAsync(CancellationToken ct = default) =>
        db.MatchCandidates.CountAsync(m => m.Status == MatchStatus.Pending, ct);

    // --- CategoryMapper ---
    public async Task<IReadOnlyList<StoreCategory>> GetStoreCategoriesAsync(CancellationToken ct = default) =>
        await db.StoreCategories.Include(c => c.Store).ToListAsync(ct);

    public async Task<IReadOnlyList<Category>> GetAllCategoriesAsync(CancellationToken ct = default) =>
        await db.Categories.ToListAsync(ct);

    public void AddCategory(Category category) => db.Categories.Add(category);

    public async Task<IReadOnlyList<Item>> GetItemsForCategorisationAsync(CancellationToken ct = default) =>
        await db.Items
            .Include(c => c.Products).ThenInclude(sp => sp.Categories)
            .Include(c => c.Products).ThenInclude(sp => sp.Store)
            .AsSplitQuery()
            .ToListAsync(ct);
}
