namespace Zhua.Application.Ingestion;

/// <summary>
/// A product as returned by a store crawler — raw, pre-persistence, pre-item-matching.
/// Price semantics: <see cref="Price"/> is what you pay now; <see cref="NonSpecialPrice"/> is the
/// regular ("was") price, set when on special so we can show the discount.
/// </summary>
public sealed record ScrapedProduct
{
    /// <summary>The store's own product id/SKU at the source.</summary>
    public required string SourceSku { get; init; }

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

    public bool IsOnSpecial { get; init; }

    public decimal? UnitPrice { get; init; }

    public string? UnitOfMeasure { get; init; }

    /// <summary>Store category path this product was crawled under: Department → Aisle → Shelf (plan D11).</summary>
    public IReadOnlyList<ScrapedCategoryNode> CategoryPath { get; init; } = [];

    /// <summary>Promo/marketing tags on this product as seen at the source (plan D13).</summary>
    public IReadOnlyList<ScrapedTag> Tags { get; init; } = [];
}
