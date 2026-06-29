namespace Zhua.Domain.Enums;

/// <summary>State of a proposed cross-store product match awaiting/after human review (plan D9/D18).</summary>
public enum MatchStatus
{
    /// <summary>Proposed by the matcher, not yet reviewed — shows up in the review queue.</summary>
    Pending = 1,

    /// <summary>A human confirmed the match; the Product is linked to the item.</summary>
    Approved = 2,

    /// <summary>A human rejected the match; never propose this pair again.</summary>
    Rejected = 3,
}
