using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("stores")]
public sealed class StoresController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>
    /// The physical stores the app tracks prices for (active only — D16 keeps duplicate Woolworths branches
    /// inactive). Optional ?supermarket=Woolworths|NewWorld|PaknSave. Used for store pickers, a map, and to
    /// label/qualify the store names that appear in product comparisons.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supermarket)
    {
        Chain? filter = Enum.TryParse<Chain>(supermarket, ignoreCase: true, out var c) ? c : null;
        if (supermarket is not null && filter is null)
            return BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

        var stores = await db.Stores
            .Where(s => s.IsActive && (filter == null || s.Chain == filter))
            .OrderBy(s => s.Chain).ThenBy(s => s.Name)
            .Select(s => new StoreView(
                s.Id, s.Chain.ToString(), s.Name, s.Suburb, s.Latitude, s.Longitude,
                s.Products.Count(sp => sp.CurrentPrice != null),
                s.CrawlRuns.Where(r => r.Status == CrawlRunStatus.Succeeded)
                    .Max(r => (DateTimeOffset?)r.FinishedAt)))
            .ToListAsync();

        return Ok(stores);
    }
}
