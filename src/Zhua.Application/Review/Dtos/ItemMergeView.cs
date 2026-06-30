namespace Zhua.Application.Review;

/// <summary>The result of a merge — what moved and the surviving item.</summary>
public sealed record ItemMergeView(Guid SourceId, Guid SurvivorId, int ProductsMoved, int CandidatesMoved);
