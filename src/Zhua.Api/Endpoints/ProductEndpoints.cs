using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        // Products filtered by canonical category (?category={id}). Same data + shape as
        // GET /categories/{id}/products — this is the "filter on the products resource" form.
        // Optional ?storeId= (repeatable) restricts to products sold at the given stores, priced within them.
        group.MapGet("/", async (Guid? category, ZhuaDbContext db, Guid[]? storeId, string sort = "unitPrice", int page = 1, int size = 20) =>
        {
            if (category is null)
                return Results.BadRequest(new { error = "query parameter 'category' is required" });

            var items = await CategoryProductQuery.RunAsync(db, category.Value, sort, page, size, storeId);
            return items is null ? Results.NotFound() : Results.Ok(items);
        });

        // Search canonical products by name or brand.
        // Optional ?storeId= (repeatable) restricts to products sold at the given stores; price/count are then
        // computed over just those stores (ids come from GET /stores).
        group.MapGet("/search", async (string? q, ZhuaDbContext db, Guid[]? storeId, int page = 1, int size = 20) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "query parameter 'q' is required" });

            size = Math.Clamp(size, 1, 100);
            page = Math.Max(page, 1);
            var like = $"%{q.Trim()}%";
            var hasStoreFilter = storeId is { Length: > 0 };

            var items = await db.CanonicalProducts
                .Where(c => EF.Functions.ILike(c.Name, like) || (c.Brand != null && EF.Functions.ILike(c.Brand, like)))
                .Where(c => !hasStoreFilter || c.StoreProducts.Any(sp => sp.CurrentPrice != null && storeId!.Contains(sp.StoreId)))
                .OrderBy(c => c.Name)
                .Skip((page - 1) * size).Take(size)
                .Select(c => new ProductSummary(
                    c.Id, c.Name, c.Description, c.Brand, c.Size, c.Category,
                    c.StoreProducts.Where(sp => sp.CurrentPrice != null && (!hasStoreFilter || storeId!.Contains(sp.StoreId)))
                        .OrderBy(sp => sp.CurrentPrice).Select(sp => sp.ImageUrl).FirstOrDefault(),
                    c.StoreProducts.Where(sp => sp.CurrentPrice != null && (!hasStoreFilter || storeId!.Contains(sp.StoreId)))
                        .Min(sp => sp.CurrentPrice),
                    c.StoreProducts.Count(sp => !hasStoreFilter || storeId!.Contains(sp.StoreId)),
                    c.StoreProducts.Any(sp => sp.IsOnSpecial && (!hasStoreFilter || storeId!.Contains(sp.StoreId))),
                    c.StoreProducts.Where(sp => sp.CurrentPrice != null && (!hasStoreFilter || storeId!.Contains(sp.StoreId)))
                        .OrderBy(sp => sp.CurrentPrice).Select(sp => (DateTimeOffset?)sp.LastSeenAt).FirstOrDefault()))
                .ToListAsync();

            return Results.Ok(items);
        });

        // Same-product cross-store comparison (cheapest first) — the core "where's it cheapest" view.
        group.MapGet("/{id:guid}", async (Guid id, ZhuaDbContext db) =>
        {
            var c = await db.CanonicalProducts.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();

            var prices = await db.StoreProducts
                .Where(sp => sp.CanonicalProductId == id)
                .OrderBy(sp => sp.CurrentPrice == null).ThenBy(sp => sp.CurrentPrice) // priced first, cheapest first
                .Select(sp => new StorePrice(
                    sp.Store.Name, sp.Store.Chain.ToString(), sp.Store.Suburb, sp.RawName, sp.ImageUrl,
                    sp.CurrentPrice, sp.IsOnSpecial, sp.CurrentNonSpecialPrice, sp.UnitPrice, sp.UnitOfMeasure,
                    sp.PriceUpdatedAt, sp.LastSeenAt))
                .ToListAsync();

            var priced = prices.Where(p => p.Price is not null).Select(p => p.Price!.Value).ToList();
            decimal? cheapest = priced.Count > 0 ? priced.Min() : null;
            decimal? saving = priced.Count > 1 ? priced.Max() - priced.Min() : null;
            var image = prices.Select(p => p.ImageUrl).FirstOrDefault(u => u != null); // cheapest-first → cheapest store's

            return Results.Ok(new ProductComparison(c.Id, c.Name, c.Description, c.Brand, c.Size, c.Category, image, cheapest, saving, prices));
        });

        // Price history: one step-series per store (change-only snapshots, D3). ?days=N caps the range.
        // Sparse by design — each point is a real price change; render as a step line (price holds until next).
        group.MapGet("/{id:guid}/price-history", async (Guid id, ZhuaDbContext db, int? days) =>
        {
            var c = await db.CanonicalProducts.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();

            var since = days is > 0 ? DateTimeOffset.UtcNow.AddDays(-days.Value) : DateTimeOffset.MinValue;

            var rows = await db.StoreProducts
                .Where(sp => sp.CanonicalProductId == id)
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

            return Results.Ok(new ProductPriceHistory(c.Id, c.Name, c.Brand, c.Size, stores));
        });

        return app;
    }
}
