namespace Zhua.Application.Review;

/// <summary>A pending cross-store match awaiting review (the queue).</summary>
public sealed record MatchCandidateView(
    Guid Id,
    Guid ProductId,             // the listing under review — target of PATCH /products/{id}
    string ProductName,
    string? Brand,
    string? Size,
    string Supermarket,
    decimal? Price,
    Guid CandidateItemId,       // the proposed item's id (approve uses it, or pre-fill a manual link)
    string CandidateItem,
    double Score,
    string? Reason);

/// <summary>Decide a pending match candidate — PATCH /match-candidates/{id}. Status = <c>approved</c> | <c>rejected</c>.</summary>
public sealed record UpdateMatchCandidateRequest(string Status);

/// <summary>A match candidate's state after a decision (ItemId is set only when approved).</summary>
public sealed record MatchCandidateDecision(Guid Id, string Status, Guid? ItemId);

/// <summary>
/// Create an item (internal join key, plan D25) — POST /items. <c>Name</c> is the grouping anchor;
/// <c>Description</c> defaults to it. The review UI pre-fills these from the listing it's creating the item for,
/// then links that listing via PATCH /products/{id}.
/// </summary>
public sealed record CreateItemRequest(string Name, string? Description, string? Brand, string? Size, string? Category);

/// <summary>An item (internal — never a shopper label) as returned by the admin create action.</summary>
public sealed record ItemView(Guid Id, string Name, string? Description, string? Brand, string? Size, string Category);
