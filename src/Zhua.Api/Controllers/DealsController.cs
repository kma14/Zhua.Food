using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("deals")]
public sealed class DealsController(IDealQueries deals) : ControllerBase
{
    /// <summary>Current specials (biggest dollar saving first). Optional ?supermarket=Woolworths|NewWorld|PaknSave.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supermarket, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        if (!SupermarketFilter.TryParse(supermarket, out var chain))
            return BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

        return Ok(await deals.ListAsync(chain, page, size));
    }
}
