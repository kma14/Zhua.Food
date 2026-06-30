using Zhua.Domain.Matching;

namespace Zhua.Domain.Services;

/// <summary>
/// The heuristic same-item rule (plan D18): score brand+size-matched candidates by name-token overlap; a single
/// clearly-best candidate ≥ <see cref="AutoLinkThreshold"/> auto-links, anything ambiguous/weaker is shortlisted for
/// review, and anything below <see cref="CandidateThreshold"/> is ignored. Tuning these knobs trades false-merges
/// against review-queue volume.
/// </summary>
public sealed class HeuristicItemMatchingPolicy : IItemMatchingPolicy
{
    public const double AutoLinkThreshold = 0.8; // name-token overlap needed to auto-link cross-chain
    public const double CandidateThreshold = 0.3; // below this we don't even propose for review
    private const double ClearWinnerMargin = 0.001;
    private const int MaxProposals = 3;

    public MatchOutcome Evaluate(HashSet<string> productTokens, IReadOnlyList<MatchTarget> candidates)
    {
        var scored = candidates
            .Select(c => new ScoredItem(c.ItemId, ProductNormalizer.TokenOverlap(productTokens, c.Tokens)))
            .Where(s => s.Score >= CandidateThreshold)
            .OrderByDescending(s => s.Score)
            .ToList();

        if (scored.Count == 0) return MatchOutcome.None;

        var clearWinner = scored.Count == 1 || scored[0].Score - scored[1].Score > ClearWinnerMargin;

        if (scored[0].Score >= AutoLinkThreshold && clearWinner)
            return new MatchOutcome(scored[0].ItemId, [], true, scored.Count);

        return new MatchOutcome(null, scored.Take(MaxProposals).ToList(), clearWinner, scored.Count);
    }
}
