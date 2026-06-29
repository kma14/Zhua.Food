using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

/// <summary>
/// Items — the internal join key that groups the same product across stores (plan D25). Never a shopper resource:
/// no public read, and the item is never shown — only its id + description anchor a grouping. Admin-only. The one
/// write is "this listing is genuinely a new product": create the item here, then link the listing via
/// PATCH /products/{id}. Guarded by the <c>Admin</c> policy (enforcement pending the auth task — see Program.cs).
/// </summary>
[ApiController]
[Route("items")]
[Authorize("Admin")]
public sealed class ItemsController(IItemService items) : ZhuaController
{
    /// <summary>
    /// Create an item from supplied fields. The review UI pre-fills <c>name</c>/<c>brand</c>/<c>size</c>/
    /// <c>category</c> from the listing it's reviewing; <c>description</c> (the grouping label, D25) defaults to
    /// <c>name</c>. Returns the new item so the caller can link it. <c>400</c> if <c>name</c> is blank.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateItemRequest body) =>
        Respond(await items.CreateAsync(body));
}
