using Zhua.Domain.Entities;

namespace Zhua.Domain.Repositories;

/// <summary>
/// Persistence port for the <see cref="Category"/> aggregate (the shared browse taxonomy, D22). Returns domain
/// entities + primitive aggregates only; the Application service builds the tree / shapes DTOs. Implemented over EF.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>Non-archived nodes (for the browse tree and for product-category subtree resolution).</summary>
    Task<IReadOnlyList<Category>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>All nodes incl. archived — for the cascade-archive walk over a subtree.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);

    /// <summary>A single node (tracked, for rename / as a create-parent).</summary>
    Task<Category?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Browse-group count per category id, matching what category browse shows (TD-5): matched items via
    /// their <c>Item.CategoryId</c> PLUS unmatched listings via their own <c>StoreCategory.CategoryId</c>, optionally
    /// scoped to stores. DB-side GROUP BY; each unmatched listing counts once per category.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountGroupsByCategoryAsync(
        IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Whether any node already occupies <paramref name="path"/> (the stable identity key).</summary>
    Task<bool> PathExistsAsync(string path, CancellationToken ct = default);

    void Add(Category category);
}
