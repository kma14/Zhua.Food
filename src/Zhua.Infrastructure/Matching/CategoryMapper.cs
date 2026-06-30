using Microsoft.EntityFrameworkCore;
using Zhua.Application.Matching;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Matching;

/// <summary>
/// Builds the shared category tree and maps products onto it (plan D22). Runs after the matcher.
/// <list type="number">
/// <item>Seed the tree from the <b>Foodstuffs</b> taxonomy (New World + PAK'nSAVE share it), deduping by path.</item>
/// <item>Map other banners' store categories in by exact (kind, slug) name match — best-effort, non-blocking.</item>
/// <item>Give each item the finest mapped category of its store products, preferring a Foodstuffs
/// member (the authoritative taxonomy). Every item has a Foodstuffs member today, so all get categorised.</item>
/// </list>
/// Idempotent: item nodes are upserted by <see cref="Category.Path"/>.
/// </summary>
public sealed class CategoryMapper(ZhuaDbContext db) : ICategoryMapper
{
    public async Task<CategoryMapResult> MapAsync(CancellationToken ct = default)
    {
        var storeCats = await db.StoreCategories.Include(c => c.Store).ToListAsync(ct);
        var byId = storeCats.ToDictionary(c => c.Id);
        var canonByPath = await db.Categories.ToDictionaryAsync(c => c.Path, ct);

        // --- 1) Build the item tree from the Foodstuffs taxonomy. Parents (Department) before children
        //        (Aisle, Shelf) so each node's parent item already exists. ---
        var foodstuffs = storeCats.Where(c => c.Store.Chain is Chain.NewWorld or Chain.PaknSave).ToList();
        foreach (var kind in (ReadOnlySpan<CategoryKind>)[CategoryKind.Department, CategoryKind.Aisle, CategoryKind.Shelf])
        {
            foreach (var sc in foodstuffs.Where(c => c.Kind == kind))
            {
                var parent = sc.ParentId is { } pid && byId.TryGetValue(pid, out var psc) ? psc.Category : null;
                var slug = Slugify(sc.Name);
                var path = parent is null ? slug : $"{parent.Path}/{slug}";

                if (!canonByPath.TryGetValue(path, out var canon))
                {
                    canon = new Category { Kind = sc.Kind, Name = sc.Name, Slug = slug, Path = path, Parent = parent };
                    db.Categories.Add(canon);
                    canonByPath[path] = canon;
                }
                sc.Category = canon;
            }
        }

        // --- 2) Map other banners' store categories in by unambiguous (kind, slug). Unmatched stay null —
        //        item PRODUCTS are categorised from their Foodstuffs member below, so this isn't blocking. ---
        var canonBySlugKind = canonByPath.Values
            .GroupBy(c => (c.Kind, c.Slug))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var sc in storeCats.Where(c => c.Store.Chain is not (Chain.NewWorld or Chain.PaknSave)))
        {
            if (sc.Category is not null) continue;
            if (canonBySlugKind.TryGetValue((sc.Kind, Slugify(sc.Name)), out var canon))
                sc.Category = canon;
        }

        // --- 3) Assign each item its finest mapped category (Foodstuffs member preferred). ---
        var products = await db.Items
            .Include(c => c.Products).ThenInclude(sp => sp.Categories)
            .Include(c => c.Products).ThenInclude(sp => sp.Store)
            .AsSplitQuery()
            .ToListAsync(ct);

        var categorized = 0;
        foreach (var cp in products)
        {
            var best = FinestMappedCategory(cp);
            if (best is null) continue;
            cp.CategoryNode = best;
            cp.Category = best.Name;
            categorized++;
        }

        await db.SaveChangesAsync(ct);

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

    // The slug rule now lives on the Category aggregate (rich-domain refactor) so curated nodes + mapped nodes agree.
    private static string Slugify(string name) => Category.Slugify(name);
}
