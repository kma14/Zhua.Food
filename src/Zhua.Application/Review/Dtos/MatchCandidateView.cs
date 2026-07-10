namespace Zhua.Application.Review;

/// <summary>A pending cross-store match awaiting review (the queue).</summary>
public sealed record MatchCandidateView(
    Guid Id,
    Guid ProductId,             // the listing under review — target of PATCH /products/{id}
    string Sku,           // source-store SKU/product id from the crawler source
    string ProductName,
    string? Brand,
    string? Size,
    string Supermarket,
    decimal? Price,
    Guid CandidateItemId,       // the proposed item's id (approve uses it, or pre-fill a manual link)
    string CandidateItem,
    double Score,
    string? Reason);
