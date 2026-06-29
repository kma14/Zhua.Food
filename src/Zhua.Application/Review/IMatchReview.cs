using Zhua.Application.Common;

namespace Zhua.Application.Review;

/// <summary>The cross-store match review queue (admin) — plan D18.</summary>
public interface IMatchReview
{
    Task<IReadOnlyList<MatchCandidateView>> PendingAsync(int page, int size);
    Task<Result<MatchCandidateDecision>> DecideAsync(Guid id, string status);
}
