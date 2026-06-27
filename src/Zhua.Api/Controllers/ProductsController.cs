using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Api.Queries;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("products")]
public sealed class ProductsController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>
    /// Products filtered by canonical category (?category={id}). Same data + shape as GET /categories/{id}/products
    /// — the "filter on the products resource" form. Optional ?storeId= (repeatable) restricts to products sold at
    /// the given stores, priced within them.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ByCategory(
        [FromQuery] Guid? category, [FromQuery] Guid[]? storeId, [FromQuery] string sort = "unitPrice", [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (category is null)
            return BadRequest(new { error = "query parameter 'category' is required" });

        var items = await CategoryProductQuery.RunAsync(db, category.Value, sort, page, size, storeId);
        return items is null ? NotFound() : Ok(items);
    }

    /// <summary>
    /// Search canonical products by name or brand. Optional ?storeId= (repeatable) restricts to products sold at
    /// the given stores; price/count are then computed over just those stores (ids come from GET /stores).
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] Guid[]? storeId, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "query parameter 'q' is required" });

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

        return Ok(items);
    }

    /// <summary>Same-product cross-store comparison (cheapest first) — the core "where's it cheapest" view.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Compare(Guid id)
    {
        var c = await db.CanonicalProducts.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();

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

        return Ok(new ProductComparison(c.Id, c.Name, c.Description, c.Brand, c.Size, c.Category, image, cheapest, saving, prices));
    }

    /// <summary>
    /// Price history: one step-series per store (change-only snapshots, D3). ?days=N caps the range. Sparse by
    /// design — each point is a real price change; render as a step line (price holds until the next).
    /// </summary>
    [HttpGet("{id:guid}/price-history")]
    public async Task<IActionResult> PriceHistory(Guid id, [FromQuery] int? days)
    {
        var c = await db.CanonicalProducts.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();

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

        return Ok(new ProductPriceHistory(c.Id, c.Name, c.Brand, c.Size, stores));
    }
}
