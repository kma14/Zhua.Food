using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("deals")]
public sealed class DealsController(IDealQueries deals) : ControllerBase
{
    /// <summary>
    /// Current specials as a paged envelope. Filters (all optional, aligned with GET /products):
    /// <c>?supermarket=</c> · <c>?category={id}</c> (the node's whole subtree; unknown/archived → 404) ·
    /// <c>?storeId=</c> (repeatable). <c>supermarket</c> + <c>storeId</c> intersect (a deal must satisfy both).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? supermarket, [FromQuery] Guid? category, [FromQuery] Guid[]? storeId,
        [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (!SupermarketFilter.TryParse(supermarket, out var chain))
            return BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

        var result = await deals.ListAsync(chain, category, storeId, page, size);
        return result is null ? NotFound() : Ok(result);
    }
}
