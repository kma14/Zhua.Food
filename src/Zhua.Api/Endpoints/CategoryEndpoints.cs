using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Application.Pricing;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories");

        // The shared canonical category tree (D22): Department → Aisle → Shelf, with product counts.
        // The front-end uses this to build category navigation. Optional ?kind= caps the depth returned
        // (Department = top level only, Aisle = two levels) so a nav menu can fetch just what it needs.
        group.MapGet("/", async (ZhuaDbContext db, string? kind) =>
        {
            var cats = await db.CanonicalCategories
                .Select(c => new { c.Id, c.Kind, c.Name, c.Slug, c.Path, c.ParentId })
                .ToListAsync();

            var counts = await db.CanonicalProducts
                .Where(p => p.CanonicalCategoryId != null)
                .GroupBy(p => p.CanonicalCategoryId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);

            var nodes = cats.ToDictionary(c => c.Id, c => new MutableNode(
                c.Id, c.Kind.ToString(), c.Name, c.Slug, c.Path, c.ParentId, counts.GetValueOrDefault(c.Id)));

            var roots = new List<MutableNode>();
            foreach (var n in nodes.Values)
            {
                if (n.ParentId is { } pid && nodes.TryGetValue(pid, out var parent)) parent.Children.Add(n);
                else roots.Add(n);
            }

            // Optional depth cap by leaf kind to return: Department (0), Aisle (1), Shelf (2 = full, default).
            var maxDepth = kind?.ToLowerInvariant() switch { "department" => 0, "aisle" => 1, _ => 2 };

            var tree = roots.OrderBy(r => r.Name).Select(r => Build(r, 0, maxDepth)).ToList();
            return Results.Ok(tree);

            static CategoryNode Build(MutableNode n, int depth, int maxDepth)
            {
                var children = depth < maxDepth
                    ? n.Children.OrderBy(c => c.Name).Select(c => Build(c, depth + 1, maxDepth)).ToList()
                    : [];
                // Total always counts the whole subtree, even when children are trimmed from the response.
                var total = n.DirectCount + SubtreeCount(n);
                return new CategoryNode(n.Id, n.Kind, n.Name, n.Slug, n.Path, n.DirectCount, total, children);
            }

            static int SubtreeCount(MutableNode n) =>
                n.Children.Sum(c => c.DirectCount + SubtreeCount(c));
        });

        // Products inside a category node (its whole subtree), each merged across stores and shown at its
        // cheapest store. ?sort=unitPrice (default, comparable per-kg/L/ea, nulls last) | price (raw cheapest).
        group.MapGet("/{id:guid}/products", async (Guid id, ZhuaDbContext db, string sort = "unitPrice", int page = 1, int size = 20) =>
        {
            size = Math.Clamp(size, 1, 100);
            page = Math.Max(page, 1);

            var cats = await db.CanonicalCategories.Select(c => new { c.Id, c.ParentId }).ToListAsync();
            if (cats.All(c => c.Id != id)) return Results.NotFound();

            // Collect the node + all descendant category ids.
            var childrenByParent = cats.Where(c => c.ParentId != null)
                .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
            var subtree = new HashSet<Guid>();
            var stack = new Stack<Guid>([id]);
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
                    cp.Id, cp.Name, cp.Brand, cp.Size,
                    Stores = cp.StoreProducts.Where(sp => sp.CurrentPrice != null).Select(sp => new
                    {
                        sp.CurrentPrice, sp.UnitPrice, sp.UnitOfMeasure, sp.RawName, sp.IsOnSpecial,
                        StoreName = sp.Store.Name, sp.Store.Chain,
                    }).ToList(),
                })
                .ToListAsync();

            var rows = products.Where(p => p.Stores.Count > 0).Select(p =>
            {
                var cheapest = p.Stores.OrderBy(s => s.CurrentPrice).First();
                var norm = UnitPriceNormalizer.ToComparable(cheapest.UnitPrice, cheapest.UnitOfMeasure);
                return new CategoryProduct(
                    p.Id, p.Name, p.Brand, p.Size, cheapest.RawName,
                    cheapest.CurrentPrice,
                    norm is { } n ? decimal.Round(n.Price, 2) : null, norm?.Unit,
                    p.Stores.Count, cheapest.StoreName, cheapest.Chain.ToString(),
                    p.Stores.Any(s => s.IsOnSpecial));
            });

            rows = sort.Equals("price", StringComparison.OrdinalIgnoreCase)
                ? rows.OrderBy(r => r.CheapestPrice)
                : rows.OrderBy(r => r.UnitPrice is null).ThenBy(r => r.UnitPrice); // comparable unit price, nulls last

            return Results.Ok(rows.Skip((page - 1) * size).Take(size).ToList());
        });

        return app;
    }

    private sealed record MutableNode(
        Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId, int DirectCount)
    {
        public List<MutableNode> Children { get; } = [];
    }
}
