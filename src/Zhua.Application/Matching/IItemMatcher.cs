namespace Zhua.Application.Matching;

/// <summary>
/// Offline item-product matcher (plan D9/D18) — runs after crawls, decoupled from crawling (R3).
/// Idempotent and re-runnable: it auto-links what it's confident about and queues the rest as
/// <see cref="Domain.Entities.MatchCandidate"/>s for human review, never re-asking an answered pair.
/// </summary>
public interface IItemMatcher
{
    Task<MatchRunResult> RunAsync(CancellationToken ct = default);
}

/// <summary>Summary of one matcher run.</summary>
public sealed record MatchRunResult(
    int Items,   // total items after the run
    int AutoLinked,          // Products auto-linked this run (Foodstuffs exact + Woolworths/FreshChoice high-confidence)
    int PendingReview,       // open candidates awaiting a human decision
    int AlreadyDecided,      // pairs skipped because a human already approved/rejected them
    int Reclaimed = 0);      // frozen FreshChoice singletons torn down to re-cascade (D30.1/TD-6)
