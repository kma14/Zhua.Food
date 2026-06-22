namespace Zhua.Application.Matching;

/// <summary>
/// Offline canonical-product matcher (plan D9/D18) — runs after crawls, decoupled from ingestion (R3).
/// Idempotent and re-runnable: it auto-links what it's confident about and queues the rest as
/// <see cref="Domain.Entities.MatchCandidate"/>s for human review, never re-asking an answered pair.
/// </summary>
public interface ICanonicalMatcher
{
    Task<MatchRunResult> RunAsync(CancellationToken ct = default);
}

/// <summary>Summary of one matcher run.</summary>
public sealed record MatchRunResult(
    int CanonicalProducts,   // total canonicals after the run
    int AutoLinked,          // StoreProducts auto-linked this run (Foodstuffs exact + Woolworths high-confidence)
    int PendingReview,       // open candidates awaiting a human decision
    int AlreadyDecided);     // pairs skipped because a human already approved/rejected them
