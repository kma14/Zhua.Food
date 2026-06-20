namespace Zhua.Domain.Entities;

/// <summary>
/// A normalized product concept = an exact item (e.g. "Tegel Tenderbasted Chicken Breast 500g").
/// Multiple <see cref="StoreProduct"/>s across stores map to one canonical product for cross-store comparison.
/// </summary>
public class CanonicalProduct
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Brand { get; set; }

    public string? Size { get; set; }

    public string? UnitOfMeasure { get; set; }

    /// <summary>Fine-grained product-type (e.g. "Chicken Breast", NOT "Chicken") — plan D9.</summary>
    public required string Category { get; set; }

    /// <summary>Barcode — primary cross-store matching key when present (plan D9).</summary>
    public string? Gtin { get; set; }

    public ICollection<StoreProduct> StoreProducts { get; } = new List<StoreProduct>();
}
