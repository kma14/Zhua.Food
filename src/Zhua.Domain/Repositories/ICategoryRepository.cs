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

    /// <summary>Item count per category id, optionally scoped to stores that have the item priced (DB-side GROUP BY).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountItemsByCategoryAsync(
        IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Whether any node already occupies <paramref name="path"/> (the stable identity key).</summary>
    Task<bool> PathExistsAsync(string path, CancellationToken ct = default);

    void Add(Category category);
}
