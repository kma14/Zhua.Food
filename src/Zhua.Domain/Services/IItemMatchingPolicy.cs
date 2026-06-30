namespace Zhua.Domain.Services;

/// <summary>A candidate item to score a product against — its id + significant name tokens.</summary>
public sealed record MatchTarget(Guid ItemId, HashSet<string> Tokens);

/// <summary>One scored candidate.</summary>
public sealed record ScoredItem(Guid ItemId, double Score);

/// <summary>
/// The matcher's decision for one product (already brand+size-filtered): either a confident <see cref="AutoLinkItemId"/>,
/// or a ranked <see cref="Proposed"/> shortlist to queue for review. <see cref="ClearWinner"/> + <see cref="ScoredCount"/>
/// describe the shortlist (for the review reason). <see cref="ScoredCount"/> 0 = nothing crossed the propose threshold.
/// </summary>
public sealed record MatchOutcome(Guid? AutoLinkItemId, IReadOnlyList<ScoredItem> Proposed, bool ClearWinner, int ScoredCount)
{
    public static readonly MatchOutcome None = new(null, [], false, 0);
}

/// <summary>
/// The pure same-item decision rule (plan D9/D18) — the platform's differentiator, as a domain service so it's
/// unit-testable without a database and swappable later (the deferred AI matcher). The heuristic implementation
/// scores by name-token overlap against confidence thresholds.
/// </summary>
public interface IItemMatchingPolicy
{
    MatchOutcome Evaluate(HashSet<string> productTokens, IReadOnlyList<MatchTarget> candidates);
}
