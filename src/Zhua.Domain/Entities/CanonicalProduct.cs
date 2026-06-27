namespace Zhua.Domain.Entities;

/// <summary>
/// A normalized product concept = an exact item (e.g. "Tegel Tenderbasted Chicken Breast 500g").
/// Multiple <see cref="StoreProduct"/>s across stores map to one canonical product for cross-store comparison.
/// </summary>
public class CanonicalProduct
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable identity for matcher re-runs (plan D18) — e.g. "foodstuffs:5103401-KGM-000". Lets the offline
    /// matcher upsert the same canonical each run so human review decisions keep pointing at it. Null for
    /// canonicals created another way.
    /// </summary>
    public string? MatchKey { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The one owned, stable phrase for this canonical (plan D25): doubles as the matcher's match anchor AND the
    /// shopper-facing grouping label ("we think these are: X"). Unlike <see cref="Name"/>, it is **never re-minted
    /// from store data** by the matcher — set on creation, then owned by us. Seeded from the representative listing.
    /// </summary>
    public string? Description { get; set; }

    public string? Brand { get; set; }

    public string? Size { get; set; }

    public string? UnitOfMeasure { get; set; }

    /// <summary>
    /// Fine-grained product-type name (e.g. "Chicken Breast", NOT "Chicken") — plan D9. Denormalized display
    /// value (the canonical category's leaf name); <see cref="CanonicalCategoryId"/> is the structured link (D22).
    /// </summary>
    public required string Category { get; set; }

    /// <summary>The shared canonical category this product sits under (plan D22) — drives UI browse/filter.</summary>
    public Guid? CanonicalCategoryId { get; set; }

    public CanonicalCategory? CanonicalCategory { get; set; }

    /// <summary>Barcode — primary cross-store matching key when present (plan D9).</summary>
    public string? Gtin { get; set; }

    public ICollection<StoreProduct> StoreProducts { get; } = new List<StoreProduct>();
}
