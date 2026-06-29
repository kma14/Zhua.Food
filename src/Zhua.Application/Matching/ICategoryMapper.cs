namespace Zhua.Application.Matching;

/// <summary>
/// Builds the shared category tree and maps products + store categories onto it (plan D22).
/// Runs offline after <see cref="IItemMatcher"/> (items must exist first) and is idempotent.
/// The tree is seeded from the Foodstuffs taxonomy (New World + PAK'nSAVE share it, covering the bulk of
/// items for free); other banners' store categories are mapped in by name where they match.
/// </summary>
public interface ICategoryMapper
{
    Task<CategoryMapResult> MapAsync(CancellationToken ct = default);
}

/// <summary>Summary of one category-mapping run.</summary>
public sealed record CategoryMapResult(
    int Categories,   // nodes in the shared tree after the run
    int MappedStoreCategories, // store categories linked to a item node
    int CategorizedProducts);  // items assigned a category
