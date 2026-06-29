using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

/// <summary>
/// The shared category tree (plan D22) — the one curated, owned vocabulary. Reads are public; the curation writes
/// (create / rename / soft-delete, plan D25 phase 3) live on the same resource, guarded by the <c>Admin</c> policy.
/// </summary>
[ApiController]
[Route("categories")]
public sealed class CategoriesController(ICategoryService categories, IProductService products) : ZhuaController
{
    /// <summary>
    /// Department → Aisle → Shelf, with product counts. Optional ?kind= caps the depth returned (Department = top
    /// level only, Aisle = two levels). Optional ?storeId= (repeatable) restricts the counts to products sold at
    /// the given stores ("what's available at my stores"; ids come from GET /stores). Archived nodes are hidden.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Tree([FromQuery] string? kind, [FromQuery] Guid[]? storeId) =>
        Ok(await categories.TreeAsync(kind, storeId));

    /// <summary>
    /// Products inside a category node (its whole subtree), grouped by item — the browse alias of
    /// GET /products?category={id} (same ProductGroup[] shape). Optional ?storeId= (repeatable). Archived id → 404.
    /// </summary>
    [HttpGet("{id:guid}/products")]
    public async Task<IActionResult> Products(
        Guid id, [FromQuery] Guid[]? storeId, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var groups = await products.ListAsync(q: null, categoryId: id, storeId, page, size);
        return groups is null ? NotFound() : Ok(groups);
    }

    /// <summary>Create a curated category. Path/slug are derived from the name (+ parent); path must be unique.</summary>
    [HttpPost]
    [Authorize("Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest body) =>
        Respond(await categories.CreateAsync(body));

    /// <summary>Rename a category's display name. Path/slug stay (the stable mapper key), so it's not re-created.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize("Admin")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameCategoryRequest body) =>
        Respond(await categories.RenameAsync(id, body));

    /// <summary>Soft-delete: archive the node + its whole subtree. Hidden from browse; survives mapper re-runs.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize("Admin")]
    public async Task<IActionResult> Archive(Guid id) =>
        Respond(await categories.ArchiveAsync(id));
}
