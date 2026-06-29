using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zhua.Api.Contracts;
using Zhua.Domain.Entities;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

/// <summary>
/// Items — the internal join key that groups the same item across stores (plan D25). Never a shopper
/// resource: there is no public read, and the item is never shown — only its id + description anchor a grouping.
/// Admin-only. The one write is "this listing is genuinely a new product": create the item here, then link the
/// listing via PATCH /store-products/{id}. Touches already-ingested data only — never crawls or migrates (CLAUDE.md).
/// </summary>
[ApiController]
[Route("items")]
[Authorize("Admin")]
public sealed class ItemsController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>
    /// Create a item from supplied fields. The review UI pre-fills <c>name</c>/<c>brand</c>/<c>size</c>/
    /// <c>category</c> from the listing it's reviewing; <c>description</c> (the owned grouping label, plan D25)
    /// defaults to <c>name</c>. Returns the new item so the caller can link it.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateItemRequest body)
    {
        var name = Clean(body.Name);
        if (name is null) return BadRequest(new { error = "name is required" });

        var item = new Item
        {
            Name = name,
            Description = Clean(body.Description) ?? name,   // owned grouping label (plan D25)
            Brand = Clean(body.Brand),
            Size = Clean(body.Size),
            Category = Clean(body.Category) ?? "Uncategorised",
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var view = new ItemView(
            item.Id, item.Name, item.Description, item.Brand, item.Size, item.Category);
        return Created($"/items/{item.Id}", view);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
