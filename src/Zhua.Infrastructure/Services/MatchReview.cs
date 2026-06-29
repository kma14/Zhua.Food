using Microsoft.EntityFrameworkCore;
using Zhua.Application.Common;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>EF implementation of <see cref="IMatchReview"/> (D27/D18) — the admin cross-store match review queue.</summary>
public sealed class MatchReview(ZhuaDbContext db, TimeProvider clock) : IMatchReview
{
    public async Task<IReadOnlyList<MatchCandidateView>> PendingAsync(int page, int size)
    {
        size = Math.Clamp(size, 1, 200);
        page = Math.Max(page, 1);

        return await db.MatchCandidates
            .Where(m => m.Status == MatchStatus.Pending)
            .OrderByDescending(m => m.Score).ThenBy(m => m.Product.RawName)
            .Skip((page - 1) * size).Take(size)
            .Select(m => new MatchCandidateView(
                m.Id, m.ProductId, m.Product.RawName, m.Product.RawBrand, m.Product.RawSize,
                m.Product.Store.Chain.ToString(), m.Product.CurrentPrice,
                m.ItemId, m.Item.Name, m.Score, m.Reason))
            .ToListAsync();
    }

    public async Task<Result<MatchCandidateDecision>> DecideAsync(Guid id, string status)
    {
        var target = (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "approved" => MatchStatus.Approved,
            "rejected" => MatchStatus.Rejected,
            _ => (MatchStatus?)null,
        };
        if (target is null)
            return Result<MatchCandidateDecision>.BadRequest("status must be 'approved' or 'rejected'");

        var m = await db.MatchCandidates.Include(x => x.Product).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return Result<MatchCandidateDecision>.NotFound("match candidate not found");
        if (m.Status != MatchStatus.Pending) return Result<MatchCandidateDecision>.Conflict($"already {m.Status}");

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
        return Result<MatchCandidateDecision>.Ok(new MatchCandidateDecision(m.Id, m.Status.ToString(), m.Product.ItemId));
    }
}
