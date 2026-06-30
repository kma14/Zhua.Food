using Zhua.Domain.Entities;

namespace Zhua.Domain.Repositories;

/// <summary>
/// Data access for the offline matching pipeline (the <c>ItemMatcher</c> + <c>CategoryMapper</c> use cases, run by
/// the Worker). Bulk loads + add/remove of the entities they mutate; the use cases commit via <see cref="IUnitOfWork"/>.
/// Returns tracked domain entities. Implemented over EF.
/// </summary>
public interface IMatchingRepository
{
    // --- ItemMatcher ---
    /// <summary>Active-store listings with Store + their StoreCategories loaded (tracked).</summary>
    Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default);

    /// <summary>Every item (tracked) — keyed by MatchKey, with merge tombstones resolved by the matcher.</summary>
    Task<IReadOnlyList<Item>> GetAllItemsAsync(CancellationToken ct = default);

    /// <summary>Every match candidate (tracked) — prior human decisions + the existing queue.</summary>
    Task<IReadOnlyList<MatchCandidate>> GetAllCandidatesAsync(CancellationToken ct = default);

    void AddItem(Item item);
    void AddCandidate(MatchCandidate candidate);
    void RemoveCandidates(IEnumerable<MatchCandidate> candidates);

    /// <summary>Pending candidates whose listing has since been linked — cleared at the end of a run.</summary>
    Task<IReadOnlyList<MatchCandidate>> GetResolvedPendingCandidatesAsync(CancellationToken ct = default);

    Task<int> CountActiveItemsAsync(CancellationToken ct = default);      // non-merged items
    Task<int> CountLinkedProductsAsync(CancellationToken ct = default);
    Task<int> CountPendingCandidatesAsync(CancellationToken ct = default);

    // --- CategoryMapper ---
    /// <summary>Store categories with their Store (tracked) — the source taxonomy to map in.</summary>
    Task<IReadOnlyList<StoreCategory>> GetStoreCategoriesAsync(CancellationToken ct = default);

    /// <summary>Every shared-tree category (tracked) — upserted by Path.</summary>
    Task<IReadOnlyList<Category>> GetAllCategoriesAsync(CancellationToken ct = default);

    void AddCategory(Category category);

    /// <summary>Items with their products' store-categories + store loaded (tracked) — for assigning each item a category.</summary>
    Task<IReadOnlyList<Item>> GetItemsForCategorisationAsync(CancellationToken ct = default);
}
