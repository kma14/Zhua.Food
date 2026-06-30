namespace Zhua.Application.Review;

/// <summary>A match candidate's state after a decision (ItemId is set only when approved).</summary>
public sealed record MatchCandidateDecision(Guid Id, string Status, Guid? ItemId);
