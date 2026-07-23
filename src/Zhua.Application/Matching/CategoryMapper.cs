using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Matching;

/// <summary>
/// Builds the shared category tree and maps products onto it (plan D22). Runs after the matcher. Orchestration only —
/// data access via <see cref="IMatchingRepository"/>, slug/path identity is <see cref="Category.Slugify"/>/
/// <see cref="Category.Create"/> on the aggregate, commits via <see cref="IUnitOfWork"/>.
/// <list type="number">
/// <item>Seed the tree from the <b>Foodstuffs</b> taxonomy (New World + PAK'nSAVE share it), deduping by path.</item>
/// <item>Map other banners' store categories in by exact (kind, slug) name match, then a curated department alias
/// table (<see cref="DepartmentAliases"/>) for WW/FC department names that don't slug-match — best-effort,
/// non-blocking.</item>
/// <item>Give each item the finest mapped category of its store products, preferring a Foodstuffs member.</item>
/// </list>
/// Idempotent: category nodes are upserted by <see cref="Category.Path"/>.
/// </summary>
public sealed class CategoryMapper(IMatchingRepository repo, IUnitOfWork uow) : ICategoryMapper
{
    /// <summary>
    /// Curated store-department → shared-department mappings (plan D30 follow-up). The shared tree is seeded from the
    /// Foodstuffs taxonomy, so Woolworths/FreshChoice departments whose names don't slug-match it (e.g. WW
    /// "Meat &amp; Poultry" vs Foodstuffs "Meat, Poultry &amp; Seafood") would leave every WW/FC-anchored item
    /// Uncategorized. Keyed by the source department's slug (<see cref="Category.Slugify"/>) → target department
    /// <see cref="Category.Path"/>. Department-level only — finer aisle/shelf mapping is bounded by the Foodstuffs
    /// tree's depth and left for later; the promo/cross-cutting WW aisles ("3 for $20", "In Season") deliberately
    /// aren't force-mapped. This is the one curated user-facing surface (D25), and it's ~7 node aliases, not per-item.
    /// </summary>
    private static readonly Dictionary<string, string> DepartmentAliases = new()
    {
        // Woolworths
        ["meat-poultry"] = "meat-poultry-seafood",
        ["fish-seafood"] = "meat-poultry-seafood",
        ["fruit-veg"] = "fruit-vegetables",
        ["fridge-deli"] = "fridge-deli-eggs",
        // FreshChoice
        ["meat"] = "meat-poultry-seafood",
        ["seafood"] = "meat-poultry-seafood",
        ["dairy-eggs"] = "fridge-deli-eggs",
    };

    public async Task<CategoryMapResult> MapAsync(CancellationToken ct = default)
    {
        var storeCats = await repo.GetStoreCategoriesAsync(ct);
        var byId = storeCats.ToDictionary(c => c.Id);
        var canonByPath = (await repo.GetAllCategoriesAsync(ct)).ToDictionary(c => c.Path);

        // --- 1) Build the shared category tree from the Foodstuffs taxonomy. Parents (Department) before children
        //        (Aisle, Shelf) so each node's parent category already exists. ---
        var foodstuffs = storeCats.Where(c => c.Store.Chain is Chain.NewWorld or Chain.PaknSave).ToList();
        foreach (var kind in (ReadOnlySpan<CategoryKind>)[CategoryKind.Department, CategoryKind.Aisle, CategoryKind.Shelf])
        {
            foreach (var sc in foodstuffs.Where(c => c.Kind == kind))
            {
                var parent = sc.ParentId is { } pid && byId.TryGetValue(pid, out var psc) ? psc.Category : null;
                var slug = Category.Slugify(sc.Name);
                var path = parent is null ? slug : $"{parent.Path}/{slug}";

                if (!canonByPath.TryGetValue(path, out var canon))
                {
                    canon = new Category { Kind = sc.Kind, Name = sc.Name, Slug = slug, Path = path, Parent = parent };
                    repo.AddCategory(canon);
                    canonByPath[path] = canon;
                }
                sc.Category = canon;
            }
        }

        // --- 2) Map other banners' store categories in by unambiguous (kind, slug), then by a curated alias table
        //        for the department names that don't slug-match (Woolworths/FreshChoice name their departments
        //        differently from the Foodstuffs taxonomy). Unmatched stay null — the items are categorised from
        //        their Foodstuffs member below, so this isn't blocking; the aliases just recover the ~885 WW/FC-
        //        anchored items (plan D30) that have no Foodstuffs member, so were Uncategorized. ---
        var canonBySlugKind = canonByPath.Values
            .GroupBy(c => (c.Kind, c.Slug))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var sc in storeCats.Where(c => c.Store.Chain is not (Chain.NewWorld or Chain.PaknSave)))
        {
            if (sc.Category is not null) continue;
            var slug = Category.Slugify(sc.Name);
            if (canonBySlugKind.TryGetValue((sc.Kind, slug), out var canon)
                || (DepartmentAliases.TryGetValue(slug, out var targetPath) && canonByPath.TryGetValue(targetPath, out canon)))
                sc.Category = canon;
        }

        // --- 3) Assign each item its finest mapped category (Foodstuffs member preferred). ---
        var products = await repo.GetItemsForCategorisationAsync(ct);

        var categorized = 0;
        foreach (var cp in products)
        {
            var best = FinestMappedCategory(cp);
            if (best is null) continue;
            cp.CategoryNode = best;
            cp.Category = best.Name;
            categorized++;
        }

        await uow.SaveChangesAsync(ct);

        return new CategoryMapResult(
            canonByPath.Count,
            storeCats.Count(c => c.CategoryId is not null),
            categorized);

        static Category? FinestMappedCategory(Item cp)
        {
            // Foodstuffs members first (authoritative taxonomy), then any member; finest kind (Shelf) first.
            foreach (var preferFoodstuffs in (ReadOnlySpan<bool>)[true, false])
            {
                foreach (var kind in (ReadOnlySpan<CategoryKind>)[CategoryKind.Shelf, CategoryKind.Aisle, CategoryKind.Department])
                {
                    foreach (var sp in cp.Products)
                    {
                        if (preferFoodstuffs != (sp.Store.Chain is Chain.NewWorld or Chain.PaknSave)) continue;
                        // Skip archived nodes (D25 phase 3) so a product bubbles up to its nearest live ancestor.
                        var c = sp.Categories.FirstOrDefault(x => x.Kind == kind
                            && x.Category is { IsArchived: false });
                        if (c is not null) return c.Category;
                    }
                }
            }
            return null;
        }
    }
}
