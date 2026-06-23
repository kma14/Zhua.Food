namespace Zhua.Domain.ValueObjects;

/// <summary>
/// One crawl's observation of a product's mutable state, as primitives (D19) — the Domain value object that
/// <see cref="Entities.StoreProduct.ApplyObservation"/> consumes. Lives in Domain (not Application's
/// <c>ScrapedProduct</c>) so the price-change invariant (D3) can be owned by the entity without Domain depending
/// on Application; the orchestrator maps <c>ScrapedProduct</c> → this.
/// </summary>
public sealed record StoreProductObservation(
    string Name,
    string? Brand,
    string? Size,
    string? Gtin,
    string? Url,
    string? ImageUrl,
    decimal Price,
    decimal? NonSpecialPrice,
    bool IsOnSpecial,
    decimal? UnitPrice,
    string? UnitOfMeasure);
