namespace Zhua.Application.Products;

/// <summary>
/// Set a product's item link — PATCH /products/{id}. An item id links it (the reviewer's manual override when no
/// candidate fits); <c>null</c> clears it (unlink).
/// </summary>
public sealed record UpdateProductLinkRequest(Guid? ItemId);
