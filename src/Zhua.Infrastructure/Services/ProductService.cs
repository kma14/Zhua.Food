using Microsoft.EntityFrameworkCore;
using Zhua.Application.Common;
using Zhua.Application.Pricing;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>
/// EF implementation of <see cref="IProductService"/> (D27) — the store-first grouped collection + the admin
/// item-link write. Groups the real per-store listings by item; computes no group aggregates (the client ranks).
/// </summary>
public sealed class ProductService(ZhuaDbContext db) : IProductService
{
    public async Task<IReadOnlyList<ProductGroup>?> ListAsync(
        string? q, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);
        var hasStoreFilter = storeIds is { Count: > 0 };

        // Resolve the category subtree (if filtering by category). Archived nodes are hidden (D25 phase 3);
        // an unknown/archived id → null → 404.
        HashSet<Guid>? subtree = null;
        if (categoryId is { } catId)
        {
            var cats = await db.Categories.Where(c => !c.IsArchived).Select(c => new { c.Id, c.ParentId }).ToListAsync();
            if (cats.All(c => c.Id != catId)) return null;
            var childrenByParent = cats.Where(c => c.ParentId != null)
                .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
            subtree = [];
            var stack = new Stack<Guid>([catId]);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!subtree.Add(n)) continue;
                if (childrenByParent.TryGetValue(n, out var ch)) foreach (var c in ch) stack.Push(c);
            }
        }

        var query = db.Products.Where(p => p.Store.IsActive && p.CurrentPrice != null);
        if (hasStoreFilter) query = query.Where(p => storeIds!.Contains(p.StoreId));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.RawName, like)
                || (p.RawBrand != null && EF.Functions.ILike(p.RawBrand, like)));
        }
        if (subtree is not null)
            query = query.Where(p =>
                p.ItemId != null && p.Item!.CategoryId != null && subtree.Contains(p.Item.CategoryId.Value));

        var groups = await BuildAsync(query);
        return groups
            .OrderBy(g => g.Description ?? g.Products[0].Name)   // stable, neutral group order — the client re-sorts
            .Skip((page - 1) * size).Take(size)
            .ToList();
    }

    public async Task<ProductGroup?> GetGroupAsync(Guid productId)
    {
        var product = await db.Products.Select(p => new { p.Id, p.ItemId }).FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return null;

        var query = product.ItemId is { } itemId
            ? db.Products.Where(p => p.ItemId == itemId)
            : db.Products.Where(p => p.Id == productId);
        return (await BuildAsync(query)).FirstOrDefault();
    }

    public async Task<ProductPriceHistory?> GetPriceHistoryAsync(Guid productId, int? days)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return null;

        var since = days is > 0 ? DateTimeOffset.UtcNow.AddDays(-days.Value) : DateTimeOffset.MinValue;
        var group = product.ItemId is { } itemId
            ? db.Products.Where(p => p.ItemId == itemId)
            : db.Products.Where(p => p.Id == productId);

        var rows = await group
            .Select(sp => new
            {
                sp.Store.Name, sp.Store.Chain, sp.Store.Suburb,
                Points = sp.PriceSnapshots
                    .Where(ps => ps.CapturedAt >= since)
                    .OrderBy(ps => ps.CapturedAt)
                    .Select(ps => new PriceHistoryPoint(ps.CapturedAt, ps.Price, ps.IsOnSpecial, ps.NonSpecialPrice, ps.UnitPrice))
                    .ToList(),
            })
            .ToListAsync();

        var stores = rows
            .Where(r => r.Points.Count > 0)
            .OrderBy(r => r.Name)
            .Select(r => new StorePriceHistory(r.Name, r.Chain.ToString(), r.Suburb, r.Points))
            .ToList();

        return new ProductPriceHistory(productId, product.RawName, product.RawBrand, product.RawSize, stores);
    }

    public async Task<Result<ProductLinkView>> LinkAsync(Guid productId, Guid? itemId)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return Result<ProductLinkView>.NotFound("product not found");

        if (itemId is { } id)
        {
            if (!await db.Items.AnyAsync(i => i.Id == id))
                return Result<ProductLinkView>.NotFound("item not found");

            product.ItemId = id;
            var pending = await db.MatchCandidates
                .Where(m => m.ProductId == productId && m.Status == MatchStatus.Pending)
                .ToListAsync();
            db.MatchCandidates.RemoveRange(pending);   // this listing is resolved now
        }
        else
        {
            product.ItemId = null;                     // unlink
        }

        await db.SaveChangesAsync();
        return Result<ProductLinkView>.Ok(new ProductLinkView(product.Id, product.ItemId));
    }

    /// <summary>Project the listings, group by item, and build each <see cref="ProductGroup"/> (no ordering/paging).</summary>
    private static async Task<List<ProductGroup>> BuildAsync(IQueryable<Product> query)
    {
        var listings = await query.Where(p => p.CurrentPrice != null).Select(p => new
        {
            p.Id, p.ItemId, p.RawName, p.RawBrand, p.RawSize, p.ImageUrl,
            p.CurrentPrice, p.CurrentNonSpecialPrice, p.IsOnSpecial, p.UnitPrice, p.UnitOfMeasure,
            StoreName = p.Store.Name, p.Store.Chain, p.Store.Suburb, p.PriceUpdatedAt, p.LastSeenAt,
            ItemDescription = p.Item != null ? p.Item.Description : null,
            ItemCategory = p.Item != null ? p.Item.Category : null,
        }).ToListAsync();

        return listings
            .GroupBy(p => p.ItemId ?? p.Id)        // matched listings collapse; unmatched = a group of one
            .Select(g =>
            {
                var any = g.First();
                var products = g
                    .OrderBy(p => p.CurrentPrice)   // a neutral default order; the client re-sorts as it likes
                    .Select(p =>
                    {
                        var norm = UnitPriceNormalizer.ToComparable(p.UnitPrice, p.UnitOfMeasure);
                        return new ProductListing(
                            p.Id, p.StoreName, p.Chain.ToString(), p.Suburb, p.RawName, p.RawBrand, p.RawSize,
                            p.ImageUrl, p.CurrentPrice, p.IsOnSpecial, p.CurrentNonSpecialPrice,
                            norm is { } n ? decimal.Round(n.Price, 2) : null, norm?.Unit,
                            p.PriceUpdatedAt, p.LastSeenAt);
                    })
                    .ToList();
                return new ProductGroup(any.ItemId, any.ItemDescription, any.ItemCategory, products);
            })
            .ToList();
    }
}
