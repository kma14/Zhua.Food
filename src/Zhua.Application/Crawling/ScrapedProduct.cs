using Zhua.Domain.Enums;

namespace Zhua.Application.Crawling;

/// <summary>
/// A product as returned by a store crawler — raw, pre-persistence, pre-item-matching.
/// Price semantics (docs/internals/promotions-model.md): <see cref="Price"/> is always the unit price a
/// cardless shopper pays now; <see cref="NonSpecialPrice"/> is the regular ("was") price when on a public
/// special; a loyalty-card price goes in <see cref="MemberPrice"/>; a multibuy ("N for $X") goes in the
/// <see cref="MultibuyQuantity"/> + <see cref="MultibuyTotal"/> pair.
/// </summary>
public sealed record ScrapedProduct
{
    /// <summary>The store's own product id/SKU at the source.</summary>
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Brand { get; init; }

    public string? Size { get; init; }

    /// <summary>Barcode, if exposed — feeds item matching (plan D9).</summary>
    public string? Gtin { get; init; }

    public string? Url { get; init; }

    public string? ImageUrl { get; init; }

    /// <summary>Fine-grained product-type / category as seen at the source (plan D9).</summary>
    public string? Category { get; init; }

    public required decimal Price { get; init; }

    public decimal? NonSpecialPrice { get; init; }

    /// <summary>The primary promotion on this listing (decision D1; precedence MemberPrice &gt; Special &gt; Multibuy).</summary>
    public PromoType PromoType { get; init; }

    /// <summary>Loyalty-card price when <see cref="PromoType"/> is MemberPrice (per unit; null for a member multibuy).</summary>
    public decimal? MemberPrice { get; init; }

    /// <summary>Multibuy "N for $X": N. Set whenever the source publishes one, whatever the primary type.</summary>
    public int? MultibuyQuantity { get; init; }

    /// <summary>Multibuy "N for $X": the total $X.</summary>
    public decimal? MultibuyTotal { get; init; }

    public decimal? UnitPrice { get; init; }

    public string? UnitOfMeasure { get; init; }

    /// <summary>Store category path this product was crawled under: Department → Aisle → Shelf (plan D11).</summary>
    public IReadOnlyList<ScrapedCategoryNode> CategoryPath { get; init; } = [];

    /// <summary>Promo/marketing tags on this product as seen at the source (plan D13).</summary>
    public IReadOnlyList<ScrapedTag> Tags { get; init; } = [];
}
