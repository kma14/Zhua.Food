using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>
/// A proposed link between a <see cref="StoreProduct"/> and a <see cref="CanonicalProduct"/> that the matcher
/// was NOT confident enough to apply automatically (plan D18) — the human review queue. Approving sets the
/// product's <see cref="StoreProduct.CanonicalProductId"/>; rejecting suppresses the pair on future runs.
/// Decisions persist across re-runs so the matcher never re-asks an answered question.
/// </summary>
public class MatchCandidate
{
    public Guid Id { get; set; }

    public Guid StoreProductId { get; set; }
    public StoreProduct StoreProduct { get; set; } = null!;

    public Guid CanonicalProductId { get; set; }
    public CanonicalProduct CanonicalProduct { get; set; } = null!;

    /// <summary>Match confidence in [0,1] — name-token overlap given equal brand + size.</summary>
    public double Score { get; set; }

    /// <summary>Why this was proposed (e.g. "brand+size match, name overlap 0.50; ambiguous (3 candidates)").</summary>
    public string? Reason { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }
}
