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
        var query = db.Products.Where(p => p.Store.IsActive && p.CurrentPrice != null);

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
            ? db.Products.Where(p => p.ItemId == id && p.CurrentPrice != null)
            : db.Products.Where(p => p.Id == productId && p.CurrentPrice != null);
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

    public async Task<IReadOnlyList<Product>> FindSpecialsAsync(
        Chain? supermarket, int page, int size, CancellationToken ct = default) =>
        await db.Products
            .Where(p => p.Store.IsActive && p.IsOnSpecial
                && p.CurrentNonSpecialPrice != null && p.CurrentPrice != null
                && (supermarket == null || p.Store.Chain == supermarket))
            .OrderByDescending(p => p.CurrentNonSpecialPrice - p.CurrentPrice)
            .Skip((page - 1) * size).Take(size)
            .Include(p => p.Store)
            .ToListAsync(ct);

    public Task<Product?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> GetByItemForUpdateAsync(Guid itemId, CancellationToken ct = default) =>
        await db.Products.Where(p => p.ItemId == itemId).ToListAsync(ct);

    public Task<bool> IsLinkableItemAsync(Guid itemId, CancellationToken ct = default) =>
        db.Items.AnyAsync(i => i.Id == itemId && i.MergedIntoId == null, ct);
}
