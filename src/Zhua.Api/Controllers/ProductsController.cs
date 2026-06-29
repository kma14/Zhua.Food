using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Api.Queries;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

/// <summary>
/// Products — the real per-store listings, grouped by item (D25). The collection searches/filters the listings and
/// returns one group per item with all its store listings; the client ranks them (cheapest / nearest / on-special),
/// the API computes no aggregates. The item is internal — only its id + description + category ride along as group
/// metadata. The single admin write (set a listing's item link) lives here too, role-guarded. Never crawls or
/// migrates (CLAUDE.md).
/// </summary>
[ApiController]
[Route("products")]
public sealed class ProductsController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>
    /// The product collection: search (<c>?q=</c>, real store names/brands), filter by <c>?category=</c> and/or
    /// <c>?storeId=</c> (repeatable), paged. Listings are grouped by item — one group per product, each with all its
    /// store listings; unmatched listings are a group of one. <c>?category=</c> returns only matched listings (the
    /// item carries the category). An unknown/archived category → 404.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] Guid? category, [FromQuery] Guid[]? storeId,
        [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var groups = await ProductQuery.RunAsync(db, q, category, storeId, page, size);
        return groups is null ? NotFound() : Ok(groups);
    }

    /// <summary>
    /// The group for one product — that listing plus its cross-store siblings (every listing sharing its item),
    /// so the client has the full "where's it cheapest" picture. <c>{id}</c> is a product id; an unmatched listing
    /// returns a group of one.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id)
    {
        var group = await ProductQuery.SingleAsync(db, id);
        return group is null ? NotFound() : Ok(group);
    }

    /// <summary>
    /// Price history for a product across its stores: one step-series per store (change-only snapshots, D3).
    /// <c>{id}</c> is a product id; the series cover its whole item group. <c>?days=N</c> caps the range.
    /// </summary>
    [HttpGet("{id:guid}/price-history")]
    public async Task<IActionResult> PriceHistory(Guid id, [FromQuery] int? days)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        var since = days is > 0 ? DateTimeOffset.UtcNow.AddDays(-days.Value) : DateTimeOffset.MinValue;
        var group = product.ItemId is { } itemId
            ? db.Products.Where(p => p.ItemId == itemId)
            : db.Products.Where(p => p.Id == id);

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

        return Ok(new ProductPriceHistory(id, product.RawName, product.RawBrand, product.RawSize, stores));
    }

    /// <summary>
    /// Admin: set this listing's item link — an item id links it (clears its pending match candidates), <c>null</c>
    /// unlinks. The reviewer's manual override when no candidate fits; to link a brand-new item, create it via
    /// <c>POST /items</c> first, then PATCH with the returned id. Guarded by the <c>Admin</c> policy.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [Authorize("Admin")]
    public async Task<IActionResult> UpdateLink(Guid id, [FromBody] UpdateProductLinkRequest body)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound(new { error = "product not found" });

        if (body.ItemId is { } itemId)
        {
            if (!await db.Items.AnyAsync(i => i.Id == itemId))
                return NotFound(new { error = "item not found" });

            product.ItemId = itemId;
            var pending = await db.MatchCandidates
                .Where(m => m.ProductId == id && m.Status == MatchStatus.Pending)
                .ToListAsync();
            db.MatchCandidates.RemoveRange(pending);   // this listing is resolved now
        }
        else
        {
            product.ItemId = null;                     // unlink
        }

        await db.SaveChangesAsync();
        return Ok(new ProductLinkView(product.Id, product.ItemId));
    }
}
