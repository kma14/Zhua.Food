using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>
/// A node in a store's own category tree (Department → Aisle → Shelf), as crawled (plan D10/D11).
/// Products are many-to-many with categories — the source places one product in several shelves/aisles.
/// </summary>
public class StoreCategory
{
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }

    public Store Store { get; set; } = null!;

    public CategoryKind Kind { get; set; }

    /// <summary>The store's own id for this node (e.g. Woolworths aisle "87"). Stable across crawls.</summary>
    public required string ExternalId { get; set; }

    /// <summary>URL slug used to query the source (e.g. "beef").</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    public Guid? ParentId { get; set; }

    public StoreCategory? Parent { get; set; }

    public ICollection<StoreCategory> Children { get; } = new List<StoreCategory>();

    public ICollection<Product> Products { get; } = new List<Product>();

    /// <summary>Which category this store node maps to (plan D22); null until mapped/unmappable.</summary>
    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }
}
