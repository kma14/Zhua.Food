using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

[ApiController]
public sealed class HealthController(ZhuaDbContext db) : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "ok", service = "zhua.api" });

    [HttpGet("/health/db")]
    public async Task<IActionResult> Db() =>
        await db.Database.CanConnectAsync()
            ? Ok(new { db = "up" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable);
}
