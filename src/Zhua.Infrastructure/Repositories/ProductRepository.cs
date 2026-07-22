using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="IProductRepository"/> — data access only; the service groups/projects.</summary>
public sealed class ProductRepository(ZhuaDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> FindListingsAsync(
        string? q, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var query = db.Products.Where(p => p.Store.IsActive && p.IsAvailable && p.CurrentPrice != null);

        if (storeIds is { Count: > 0 })
            query = query.Where(p => storeIds.Contains(p.StoreId));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.RawName, like)
                || (p.RawBrand != null && EF.Functions.ILike(p.RawBrand, like)));
        }

        if (categoryIds is { Count: > 0 })
            query = query.Where(p =>
                p.ItemId != null && p.Item!.CategoryId != null && categoryIds.Contains(p.Item.CategoryId.Value));

        return await query.Include(p => p.Store).Include(p => p.Item).ToListAsync(ct);
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> FindGroupAsync(Guid? itemId, Guid productId, CancellationToken ct = default)
    {
        var query = itemId is { } id
            ? db.Products.Where(p => p.ItemId == id && p.IsAvailable && p.CurrentPrice != null)
            : db.Products.Where(p => p.Id == productId && p.IsAvailable && p.CurrentPrice != null);
        return await query.Include(p => p.Store).Include(p => p.Item).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Product>> FindGroupWithHistoryAsync(
        Guid? itemId, Guid productId, DateTimeOffset since, CancellationToken ct = default)
    {
        var query = itemId is { } id
            ? db.Products.Where(p => p.ItemId == id)
            : db.Products.Where(p => p.Id == productId);
        return await query
            .Include(p => p.Store)
            .Include(p => p.PriceSnapshots.Where(ps => ps.CapturedAt >= since))
            .ToListAsync(ct);
    }

    // The specials filter, shared by the page + the count so the two can't drift. Any current promotion is a deal —
    // the was-price is optional (Foodstuffs often has no recoverable regular price until history accumulates, D23).
    // The category/store predicates mirror FindListingsAsync exactly so /deals filters like /products.
    // seenSince is the D28 freshness guard: a special is only "current" if a crawl confirmed it recently.
    private IQueryable<Product> SpecialsQuery(
        Chain? supermarket, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds,
        DateTimeOffset seenSince)
    {
        var query = db.Products.Where(p =>
            p.Store.IsActive && p.IsAvailable && p.IsOnSpecial && p.CurrentPrice != null && p.LastSeenAt >= seenSince);
        if (supermarket is { } chain)
            query = query.Where(p => p.Store.Chain == chain);
        if (storeIds is { Count: > 0 })
            query = query.Where(p => storeIds.Contains(p.StoreId));
        if (categoryIds is { Count: > 0 })
            query = query.Where(p =>
                p.ItemId != null && p.Item!.CategoryId != null && categoryIds.Contains(p.Item.CategoryId.Value));
        return query;
    }

    public async Task<IReadOnlyList<Product>> FindSpecialsAsync(
        Chain? supermarket, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds,
        DateTimeOffset seenSince, int page, int size, CancellationToken ct = default) =>
        await SpecialsQuery(supermarket, categoryIds, storeIds, seenSince)
            .OrderByDescending(p => p.CurrentNonSpecialPrice != null)          // deals with a known saving first
            .ThenByDescending(p => p.CurrentNonSpecialPrice - p.CurrentPrice)  // biggest saving first
            .Skip((page - 1) * size).Take(size)
            .Include(p => p.Store)
            .ToListAsync(ct);

    public Task<int> CountSpecialsAsync(
        Chain? supermarket, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds,
        DateTimeOffset seenSince, CancellationToken ct = default) =>
        SpecialsQuery(supermarket, categoryIds, storeIds, seenSince).CountAsync(ct);

    public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> GetByItemForUpdateAsync(Guid itemId, CancellationToken ct = default) =>
        await db.Products.Where(p => p.ItemId == itemId).ToListAsync(ct);

    public Task<bool> IsLinkableItemAsync(Guid itemId, CancellationToken ct = default) =>
        db.Items.AnyAsync(i => i.Id == itemId && i.MergedIntoId == null, ct);

    public async Task<IReadOnlyList<MatchStatusCount>> CountByMatchStatusAsync(CancellationToken ct = default) =>
        await db.Products
            .Where(p => p.Store.IsActive)
            .GroupBy(p => new
            {
                p.Store.Chain,
                // Mechanical MatchKey prefix (e.g. "foodstuffs") — no chain-name semantics here; the Application
                // report maps scheme → status. Null when the listing isn't linked to an item.
                Scheme = p.Item == null ? null : p.Item.MatchKey!.Substring(0, p.Item.MatchKey.IndexOf(":")),
                HasPending = db.MatchCandidates.Any(m => m.ProductId == p.Id && m.Status == MatchStatus.Pending),
            })
            .Select(g => new MatchStatusCount(g.Key.Chain, g.Key.Scheme, g.Key.HasPending, g.Count()))
            .ToListAsync(ct);
}
