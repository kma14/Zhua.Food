using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

[ApiController]
public sealed class HealthController(IHealthQueries health) : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "ok", service = "zhua.api" });

    [HttpGet("/health/db")]
    public async Task<IActionResult> Db() =>
        await health.CanConnectAsync()
            ? Ok(new { db = "up" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable);
}
