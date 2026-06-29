using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>
/// A proposed link between a <see cref="Product"/> and a <see cref="Item"/> that the matcher
/// was NOT confident enough to apply automatically (plan D18) — the human review queue. Approving sets the
/// product's <see cref="Product.ItemId"/>; rejecting suppresses the pair on future runs.
/// Decisions persist across re-runs so the matcher never re-asks an answered question.
/// </summary>
public class MatchCandidate
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    /// <summary>Match confidence in [0,1] — name-token overlap given equal brand + size.</summary>
    public double Score { get; set; }

    /// <summary>Why this was proposed (e.g. "brand+size match, name overlap 0.50; ambiguous (3 candidates)").</summary>
    public string? Reason { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Human confirms the match (D18). The caller also sets the product's ItemId.</summary>
    public void Approve(DateTimeOffset now)
    {
        Status = MatchStatus.Approved;
        ReviewedAt = now;
    }

    /// <summary>Human rejects the match (D18) — the matcher won't propose this pair again.</summary>
    public void Reject(DateTimeOffset now)
    {
        Status = MatchStatus.Rejected;
        ReviewedAt = now;
    }
}
