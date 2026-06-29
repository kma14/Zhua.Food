using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

/// <summary>
/// Products — the real per-store listings, grouped by item (D25). The collection searches/filters the listings and
/// returns one group per item with all its store listings; the client ranks them, the API computes no aggregates.
/// The item is internal — only its id + description + category ride along as group metadata. The single admin write
/// (set a listing's item link) lives here too, role-guarded.
/// </summary>
[ApiController]
[Route("products")]
public sealed class ProductsController(IProductService products) : ZhuaController
{
    /// <summary>
    /// The product collection: search (<c>?q=</c>, real store names/brands), filter by <c>?category=</c> and/or
    /// <c>?storeId=</c> (repeatable), paged. Listings are grouped by item — one group per product, each with all its
    /// store listings; unmatched listings are a group of one. An unknown/archived category → 404.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] Guid? category, [FromQuery] Guid[]? storeId,
        [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var groups = await products.ListAsync(q, category, storeId, page, size);
        return groups is null ? NotFound() : Ok(groups);
    }

    /// <summary>
    /// The group for one product — that listing plus its cross-store siblings (every listing sharing its item).
    /// <c>{id}</c> is a product id; an unmatched listing returns a group of one. 404 if the product id is unknown.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id)
    {
        var group = await products.GetGroupAsync(id);
        return group is null ? NotFound() : Ok(group);
    }

    /// <summary>
    /// Price history for a product across its stores: one step-series per store (change-only snapshots, D3).
    /// <c>{id}</c> is a product id; the series cover its whole item group. <c>?days=N</c> caps the range.
    /// </summary>
    [HttpGet("{id:guid}/price-history")]
    public async Task<IActionResult> PriceHistory(Guid id, [FromQuery] int? days)
    {
        var history = await products.GetPriceHistoryAsync(id, days);
        return history is null ? NotFound() : Ok(history);
    }

    /// <summary>
    /// Admin: set this listing's item link — an item id links it (clears its pending match candidates), <c>null</c>
    /// unlinks. To link a brand-new item, create it via POST /items first, then PATCH with the returned id.
    /// Guarded by the <c>Admin</c> policy.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [Authorize("Admin")]
    public async Task<IActionResult> UpdateLink(Guid id, [FromBody] UpdateProductLinkRequest body) =>
        Respond(await products.LinkAsync(id, body.ItemId));
}
