using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Zhua.Api.Controllers;

/// <summary>
/// The cross-store match review queue (plan D18) — a wholly-admin resource (no public face). Guarded by the
/// <c>Admin</c> policy (enforcement pending the auth task — see Program.cs).
/// </summary>
[ApiController]
[Route("match-candidates")]
[Authorize("Admin")]
public sealed class MatchCandidatesController(IMatchReview review) : ZhuaController
{
    /// <summary>The pending queue (highest-confidence first).</summary>
    [HttpGet]
    public async Task<IActionResult> Pending([FromQuery] int page = 1, [FromQuery] int size = 50) =>
        Ok(await review.PendingAsync(page, size));

    /// <summary>
    /// Decide a candidate by moving its status. <c>approved</c> links the listing to the proposed item and clears
    /// the listing's other pending candidates; <c>rejected</c> tells the matcher not to propose this pair again.
    /// Any other status → 400; an already-decided candidate → 409.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] UpdateMatchCandidateRequest body) =>
        Respond(await review.DecideAsync(id, body.Status));
}
