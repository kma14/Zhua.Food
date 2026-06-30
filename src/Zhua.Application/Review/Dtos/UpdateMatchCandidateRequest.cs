namespace Zhua.Application.Review;

/// <summary>Decide a pending match candidate — PATCH /match-candidates/{id}. Status = <c>approved</c> | <c>rejected</c>.</summary>
public sealed record UpdateMatchCandidateRequest(string Status);
