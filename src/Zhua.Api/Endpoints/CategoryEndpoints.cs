using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
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
        // Optional ?storeId= (repeatable) restricts the counts to products sold at the given stores, so the nav
        // can reflect "what's available at my stores" (ids come from GET /stores).
        group.MapGet("/", async (ZhuaDbContext db, string? kind, Guid[]? storeId) =>
        {
            var storeIds = storeId;
            var hasStoreFilter = storeIds is { Length: > 0 };

            var cats = await db.CanonicalCategories
                .Select(c => new { c.Id, c.Kind, c.Name, c.Slug, c.Path, c.ParentId })
                .ToListAsync();

            var counts = await db.CanonicalProducts
                .Where(p => p.CanonicalCategoryId != null
                    && (!hasStoreFilter || p.StoreProducts.Any(sp => sp.CurrentPrice != null && storeIds!.Contains(sp.StoreId))))
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
        // Optional ?storeId= (repeatable) restricts to products sold at the given stores, priced within them.
        // Same data as GET /products?category={id} (shared query).
        group.MapGet("/{id:guid}/products", async (Guid id, ZhuaDbContext db, Guid[]? storeId, string sort = "unitPrice", int page = 1, int size = 20) =>
        {
            var items = await CategoryProductQuery.RunAsync(db, id, sort, page, size, storeId);
            return items is null ? Results.NotFound() : Results.Ok(items);
        });

        return app;
    }

    private sealed record MutableNode(
        Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId, int DirectCount)
    {
        public List<MutableNode> Children { get; } = [];
    }
}
