using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        // Search canonical products by name or brand.
        group.MapGet("/search", async (string? q, ZhuaDbContext db, int page = 1, int size = 20) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "query parameter 'q' is required" });

            size = Math.Clamp(size, 1, 100);
            page = Math.Max(page, 1);
            var like = $"%{q.Trim()}%";

            var items = await db.CanonicalProducts
                .Where(c => EF.Functions.ILike(c.Name, like) || (c.Brand != null && EF.Functions.ILike(c.Brand, like)))
                .OrderBy(c => c.Name)
                .Skip((page - 1) * size).Take(size)
                .Select(c => new ProductSummary(
                    c.Id, c.Name, c.Brand, c.Size, c.Category,
                    c.StoreProducts.Where(sp => sp.CurrentPrice != null).Min(sp => sp.CurrentPrice),
                    c.StoreProducts.Count,
                    c.StoreProducts.Any(sp => sp.IsOnSpecial)))
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
                    sp.Store.Name, sp.Store.Chain.ToString(), sp.Store.Suburb, sp.RawName,
                    sp.CurrentPrice, sp.IsOnSpecial, sp.CurrentNonSpecialPrice, sp.UnitPrice, sp.UnitOfMeasure, sp.LastSeenAt))
                .ToListAsync();

            var priced = prices.Where(p => p.Price is not null).Select(p => p.Price!.Value).ToList();
            decimal? cheapest = priced.Count > 0 ? priced.Min() : null;
            decimal? saving = priced.Count > 1 ? priced.Max() - priced.Min() : null;

            return Results.Ok(new ProductComparison(c.Id, c.Name, c.Brand, c.Size, c.Category, cheapest, saving, prices));
        });

        return app;
    }
}
