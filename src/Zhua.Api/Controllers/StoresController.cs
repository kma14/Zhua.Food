using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("stores")]
public sealed class StoresController(IStoreQueries stores) : ControllerBase
{
    /// <summary>
    /// The physical stores the app tracks prices for (active only — D16 keeps duplicate Woolworths branches
    /// inactive). Optional ?supermarket=Woolworths|NewWorld|PaknSave. Used for store pickers, a map, and to
    /// label/qualify the store names that appear in product comparisons.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supermarket)
    {
        if (!SupermarketFilter.TryParse(supermarket, out var chain))
            return BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

        return Ok(await stores.ListAsync(chain));
    }
}
