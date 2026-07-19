using Zhua.Domain.Enums;

namespace Zhua.Domain.ValueObjects;

/// <summary>
/// One crawl's observation of a product's mutable state, as primitives (D19) — the Domain value object that
/// <see cref="Entities.Product.ApplyObservation"/> consumes. Lives in Domain (not Application's
/// <c>ScrapedProduct</c>) so the price-change invariant (D3) can be owned by the entity without Domain depending
/// on Application; the orchestrator maps <c>ScrapedProduct</c> → this.
/// Price semantics (docs/internals/promotions-model.md): <paramref name="Price"/> is always the unit price a
/// cardless shopper pays now; a member/club price arrives separately in <paramref name="MemberPrice"/>; a
/// multibuy ("N for $X") arrives as the <paramref name="MultibuyQuantity"/> + <paramref name="MultibuyTotal"/> pair.
/// </summary>
public sealed record ProductObservation(
    string Name,
    string? Brand,
    string? Size,
    string? Gtin,
    string? Url,
    string? ImageUrl,
    decimal Price,
    decimal? NonSpecialPrice,
    PromoType PromoType,
    decimal? MemberPrice,
    int? MultibuyQuantity,
    decimal? MultibuyTotal,
    decimal? UnitPrice,
    string? UnitOfMeasure);
