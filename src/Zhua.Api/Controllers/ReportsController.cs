using Microsoft.AspNetCore.Mvc;
using Zhua.Application.Reporting;

namespace Zhua.Api.Controllers;

[ApiController]
[Route("reports")]
public sealed class ReportsController(IReportQueries reports) : ControllerBase
{
    /// <summary>
    /// Internal ops report (D30.1): every active-store listing counted per supermarket by match status —
    /// aggregated (foodstuffs/woolworths/freshchoice/manual item), 待审商品 (pending review), 悬空商品 (held) —
    /// as one table with a grand-total row. Not a shopper surface (items are internal, D25); it's how we see the
    /// matcher's coverage per chain.
    /// </summary>
    [HttpGet("product-status")]
    public async Task<IActionResult> ProductStatus() => Ok(await reports.ProductStatusAsync());
}
