using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Application.Pricing;
using Zhua.Domain.Entities;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Queries;

/// <summary>
/// The product collection (D25). Searches/filters the real per-store listings (<c>Product</c>) and groups them by
/// item (<c>Product.ItemId</c>) so the same product across stores comes back as one group with all its listings —
/// the client ranks them (cheapest / nearest / on-special); the API computes no group aggregates. Unmatched listings
/// are a group of one. Backs <c>GET /products</c> and <c>GET /categories/{id}/products</c>. The item is internal —
/// only its <c>id</c> + <c>description</c> + <c>category</c> ride along as group metadata.
/// </summary>
internal static class ProductQuery
{
    /// <summary>The filtered, paged product collection grouped by item.</summary>
    /// <returns><c>null</c> only when <paramref name="categoryId"/> is given but unknown/archived (caller → 404).</returns>
    public static async Task<List<ProductGroup>?> RunAsync(
        ZhuaDbContext db, string? q, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size)
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

    /// <summary>The single group containing <paramref name="productId"/> (its cross-store listings), or <c>null</c>.</summary>
    public static async Task<ProductGroup?> SingleAsync(ZhuaDbContext db, Guid productId)
    {
        var product = await db.Products.Select(p => new { p.Id, p.ItemId }).FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return null;

        var query = product.ItemId is { } itemId
            ? db.Products.Where(p => p.ItemId == itemId)
            : db.Products.Where(p => p.Id == productId);
        return (await BuildAsync(query)).FirstOrDefault();
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
