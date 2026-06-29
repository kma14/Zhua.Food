using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("deals")]
public sealed class DealsController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>Current specials (biggest dollar saving first). Optional ?supermarket=Woolworths|NewWorld|PaknSave.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? supermarket, [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);

        Chain? chainFilter = Enum.TryParse<Chain>(supermarket, ignoreCase: true, out var c) ? c : null;
        if (supermarket is not null && chainFilter is null)
            return BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

        var deals = await db.Products
            .Where(sp => sp.Store.IsActive
                && sp.IsOnSpecial
                && sp.CurrentNonSpecialPrice != null
                && sp.CurrentPrice != null
                && (chainFilter == null || sp.Store.Chain == chainFilter))
            .OrderByDescending(sp => sp.CurrentNonSpecialPrice - sp.CurrentPrice)
            .Skip((page - 1) * size).Take(size)
            .Select(sp => new DealItem(
                sp.RawName, sp.RawBrand, sp.ImageUrl, sp.Store.Name, sp.Store.Chain.ToString(),
                sp.CurrentPrice, sp.CurrentNonSpecialPrice, sp.CurrentNonSpecialPrice - sp.CurrentPrice,
                sp.UnitPrice, sp.UnitOfMeasure, sp.PriceUpdatedAt, sp.LastSeenAt))
            .ToListAsync();

        return Ok(deals);
    }
}
