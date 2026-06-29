namespace Zhua.Domain.Entities;

/// <summary>
/// A normalized product concept = an exact item (e.g. "Tegel Tenderbasted Chicken Breast 500g").
/// Multiple <see cref="Product"/>s across stores map to one item for cross-store comparison.
/// </summary>
public class Item
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable identity for matcher re-runs (plan D18) — e.g. "foodstuffs:5103401-KGM-000". Lets the offline
    /// matcher upsert the same item each run so human review decisions keep pointing at it. Null for
    /// items created another way.
    /// </summary>
    public string? MatchKey { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The one owned, stable phrase for this item (plan D25): doubles as the matcher's match anchor AND the
    /// shopper-facing grouping label ("we think these are: X"). Unlike <see cref="Name"/>, it is **never re-minted
    /// from store data** by the matcher — set on creation, then owned by us. Seeded from the representative listing.
    /// </summary>
    public string? Description { get; set; }

    public string? Brand { get; set; }

    public string? Size { get; set; }

    public string? UnitOfMeasure { get; set; }

    /// <summary>
    /// Fine-grained product-type name (e.g. "Chicken Breast", NOT "Chicken") — plan D9. Denormalized display
    /// value (the category's leaf name); <see cref="CategoryId"/> is the structured link (D22).
    /// </summary>
    public required string Category { get; set; }

    /// <summary>The shared category this product sits under (plan D22) — drives UI browse/filter.</summary>
    public Guid? CategoryId { get; set; }

    public Category? CategoryNode { get; set; }

    /// <summary>Barcode — primary cross-store matching key when present (plan D9).</summary>
    public string? Gtin { get; set; }

    public ICollection<Product> Products { get; } = new List<Product>();
}
