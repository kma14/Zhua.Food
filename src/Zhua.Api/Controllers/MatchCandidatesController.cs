using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

/// <summary>
/// The cross-store match review queue (plan D18) — a wholly-admin resource (no public face). These are among the
/// only writes the Api makes; they touch already-ingested data, never crawl or migrate (CLAUDE.md). Guarded by the
/// <c>Admin</c> policy (enforcement pending the auth task — see Program.cs).
/// </summary>
[ApiController]
[Route("match-candidates")]
[Authorize("Admin")]
public sealed class MatchCandidatesController(ZhuaDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>The pending queue (highest-confidence first).</summary>
    [HttpGet]
    public async Task<IActionResult> Pending([FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        size = Math.Clamp(size, 1, 200);
        page = Math.Max(page, 1);

        var items = await db.MatchCandidates
            .Where(m => m.Status == MatchStatus.Pending)
            .OrderByDescending(m => m.Score).ThenBy(m => m.Product.RawName)
            .Skip((page - 1) * size).Take(size)
            .Select(m => new MatchCandidateView(
                m.Id, m.ProductId, m.Product.RawName, m.Product.RawBrand, m.Product.RawSize,
                m.Product.Store.Chain.ToString(), m.Product.CurrentPrice,
                m.ItemId, m.Item.Name, m.Score, m.Reason))
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Decide a candidate by moving its status. <c>approved</c> links the listing to the proposed item and
    /// clears the listing's other pending candidates; <c>rejected</c> tells the matcher not to propose this pair
    /// again. Any other status → 400; an already-decided candidate → 409.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] UpdateMatchCandidateRequest body)
    {
        var target = (body.Status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "approved" => MatchStatus.Approved,
            "rejected" => MatchStatus.Rejected,
            _ => (MatchStatus?)null,
        };
        if (target is null)
            return BadRequest(new { error = "status must be 'approved' or 'rejected'" });

        var m = await db.MatchCandidates.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        if (m.Status != MatchStatus.Pending) return Conflict(new { error = $"already {m.Status}" });

        if (target == MatchStatus.Approved)
        {
            m.Approve(clock.GetUtcNow());
            m.Product.ItemId = m.ItemId;

            var siblings = await db.MatchCandidates
                .Where(x => x.ProductId == m.ProductId && x.Id != m.Id && x.Status == MatchStatus.Pending)
                .ToListAsync();
            db.MatchCandidates.RemoveRange(siblings);
        }
        else
        {
            m.Reject(clock.GetUtcNow());
        }

        await db.SaveChangesAsync();
        return Ok(new MatchCandidateDecision(m.Id, m.Status.ToString(), m.Product.ItemId));
    }
}
