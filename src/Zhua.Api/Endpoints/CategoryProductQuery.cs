using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Application.Pricing;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

/// <summary>
/// Shared "products inside a category node" query — backs both <c>GET /categories/{id}/products</c> and
/// <c>GET /products?category={id}</c>. Returns canonical products in the node's whole subtree, each merged
/// across stores and shown at its cheapest store, with a normalised comparable unit price + price dates.
/// </summary>
internal static class CategoryProductQuery
{
    /// <returns><c>null</c> if the category id doesn't exist; otherwise the requested page.</returns>
    /// <param name="storeIds">If non-empty, restrict to products sold at these stores; price/cheapest/count
    /// are then computed over just those stores (D16 — Foodstuffs branches are independently priced).</param>
    public static async Task<List<CategoryProduct>?> RunAsync(
        ZhuaDbContext db, Guid categoryId, string sort, int page, int size, IReadOnlyList<Guid>? storeIds = null)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);
        var hasStoreFilter = storeIds is { Count: > 0 };

        var cats = await db.CanonicalCategories.Select(c => new { c.Id, c.ParentId }).ToListAsync();
        if (cats.All(c => c.Id != categoryId)) return null;

        // The node + all its descendants.
        var childrenByParent = cats.Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var subtree = new HashSet<Guid>();
        var stack = new Stack<Guid>([categoryId]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!subtree.Add(n)) continue;
            if (childrenByParent.TryGetValue(n, out var ch)) foreach (var c in ch) stack.Push(c);
        }

        var products = await db.CanonicalProducts
            .Where(cp => cp.CanonicalCategoryId != null && subtree.Contains(cp.CanonicalCategoryId.Value))
            .Select(cp => new
            {
                cp.Id, cp.Name, cp.Description, cp.Brand, cp.Size,
                Stores = cp.StoreProducts
                    .Where(sp => sp.CurrentPrice != null && (!hasStoreFilter || storeIds!.Contains(sp.StoreId)))
                    .Select(sp => new
                    {
                        sp.CurrentPrice, sp.UnitPrice, sp.UnitOfMeasure, sp.RawName, sp.IsOnSpecial, sp.ImageUrl,
                        StoreName = sp.Store.Name, sp.Store.Chain, sp.PriceUpdatedAt, sp.LastSeenAt,
                    }).ToList(),
            })
            .ToListAsync();

        var rows = products.Where(p => p.Stores.Count > 0).Select(p =>
        {
            var cheapest = p.Stores.OrderBy(s => s.CurrentPrice).First();
            var norm = UnitPriceNormalizer.ToComparable(cheapest.UnitPrice, cheapest.UnitOfMeasure);
            var image = cheapest.ImageUrl ?? p.Stores.Select(s => s.ImageUrl).FirstOrDefault(u => u != null);
            return new CategoryProduct(
                p.Id, p.Name, p.Description, p.Brand, p.Size, image, cheapest.RawName,
                cheapest.CurrentPrice,
                norm is { } n ? decimal.Round(n.Price, 2) : null, norm?.Unit,
                p.Stores.Count, cheapest.StoreName, cheapest.Chain.ToString(),
                p.Stores.Any(s => s.IsOnSpecial),
                cheapest.PriceUpdatedAt, cheapest.LastSeenAt);
        });

        rows = sort.Equals("price", StringComparison.OrdinalIgnoreCase)
            ? rows.OrderBy(r => r.CheapestPrice)
            : rows.OrderBy(r => r.UnitPrice is null).ThenBy(r => r.UnitPrice); // comparable unit price, nulls last

        return rows.Skip((page - 1) * size).Take(size).ToList();
    }
}
