using Zhua.Application.Common;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Review;

/// <summary>
/// The admin cross-store match review queue (D27/D18). Reads the pending queue and applies approve/reject decisions
/// over <see cref="IMatchCandidateRepository"/>; the per-candidate state change is a rich-domain method
/// (<c>MatchCandidate.Approve/Reject</c>), committed through <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class MatchReview(IMatchCandidateRepository candidates, IUnitOfWork uow, TimeProvider clock) : IMatchReview
{
    public async Task<IReadOnlyList<MatchCandidateView>> PendingAsync(int page, int size)
    {
        size = Math.Clamp(size, 1, 200);
        page = Math.Max(page, 1);

        var rows = await candidates.GetPendingAsync(page, size);
        return rows.Select(m => new MatchCandidateView(
                m.Id, m.ProductId, m.Product.SourceSku, m.Product.RawName, m.Product.RawBrand, m.Product.RawSize,
                m.Product.Store.Chain.ToString(), m.Product.CurrentPrice,
                m.ItemId, m.Item.Name, m.Score, m.Reason))
            .ToList();
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

        var m = await candidates.GetForUpdateAsync(id);
        if (m is null) return Result<MatchCandidateDecision>.NotFound("match candidate not found");
        if (m.Status != MatchStatus.Pending) return Result<MatchCandidateDecision>.Conflict($"already {m.Status}");

        if (target == MatchStatus.Approved)
        {
            m.Approve(clock.GetUtcNow());
            m.Product.ItemId = m.ItemId;

            // The listing is resolved → clear its other still-pending candidates.
            foreach (var sibling in await candidates.GetPendingByProductAsync(m.ProductId))
                if (sibling.Id != m.Id)
                    candidates.Remove(sibling);
        }
        else
        {
            m.Reject(clock.GetUtcNow());
        }

        await uow.SaveChangesAsync();
        return Result<MatchCandidateDecision>.Ok(new MatchCandidateDecision(m.Id, m.Status.ToString(), m.Product.ItemId));
    }
}
