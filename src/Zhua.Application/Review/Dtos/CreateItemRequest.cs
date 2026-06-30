namespace Zhua.Application.Review;

/// <summary>
/// Create an item (internal join key, plan D25) — POST /items. <c>Name</c> is the grouping anchor;
/// <c>Description</c> defaults to it. The review UI pre-fills these from the listing it's creating the item for,
/// then links that listing via PATCH /products/{id}.
/// </summary>
public sealed record CreateItemRequest(string Name, string? Description, string? Brand, string? Size, string? Category);
