namespace Zhua.Application.Matching;

/// <summary>
/// Builds the shared canonical category tree and maps products + store categories onto it (plan D22).
/// Runs offline after <see cref="ICanonicalMatcher"/> (canonicals must exist first) and is idempotent.
/// The tree is seeded from the Foodstuffs taxonomy (New World + PAK'nSAVE share it, covering the bulk of
/// canonical products for free); other banners' store categories are mapped in by name where they match.
/// </summary>
public interface ICanonicalCategoryMapper
{
    Task<CanonicalCategoryMapResult> MapAsync(CancellationToken ct = default);
}

/// <summary>Summary of one category-mapping run.</summary>
public sealed record CanonicalCategoryMapResult(
    int CanonicalCategories,   // nodes in the shared tree after the run
    int MappedStoreCategories, // store categories linked to a canonical node
    int CategorizedProducts);  // canonical products assigned a canonical category
