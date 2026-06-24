using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>
/// A node in the cross-store canonical category tree (Department → Aisle → Shelf) — plan D22.
/// Unlike <see cref="StoreCategory"/> (one tree per store, each banner's own taxonomy), this is a single
/// shared taxonomy every product maps to, so the UI can browse/filter the whole catalogue by one category.
/// The tree is seeded from the Foodstuffs taxonomy (shared by New World + PAK'nSAVE, so it already covers
/// the bulk of products); other banners' <see cref="StoreCategory"/>s are mapped into it.
/// </summary>
public class CanonicalCategory
{
    public Guid Id { get; set; }

    public CategoryKind Kind { get; set; }

    public required string Name { get; set; }

    /// <summary>Full slug path from the root, e.g. "meat-poultry-seafood/beef/beef-steaks". Stable upsert key.</summary>
    public required string Path { get; set; }

    public required string Slug { get; set; }

    public Guid? ParentId { get; set; }

    public CanonicalCategory? Parent { get; set; }

    public ICollection<CanonicalCategory> Children { get; } = new List<CanonicalCategory>();

    /// <summary>Per-store categories mapped to this canonical node (Foodstuffs by identity, others fuzzy — D22).</summary>
    public ICollection<StoreCategory> StoreCategories { get; } = new List<StoreCategory>();

    public ICollection<CanonicalProduct> Products { get; } = new List<CanonicalProduct>();
}
