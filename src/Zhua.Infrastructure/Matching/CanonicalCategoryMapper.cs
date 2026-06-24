using System.Text;
using Microsoft.EntityFrameworkCore;
using Zhua.Application.Matching;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Matching;

/// <summary>
/// Builds the shared canonical category tree and maps products onto it (plan D22). Runs after the matcher.
/// <list type="number">
/// <item>Seed the tree from the <b>Foodstuffs</b> taxonomy (New World + PAK'nSAVE share it), deduping by path.</item>
/// <item>Map other banners' store categories in by exact (kind, slug) name match — best-effort, non-blocking.</item>
/// <item>Give each canonical product the finest mapped category of its store products, preferring a Foodstuffs
/// member (the authoritative taxonomy). Every canonical has a Foodstuffs member today, so all get categorised.</item>
/// </list>
/// Idempotent: canonical nodes are upserted by <see cref="CanonicalCategory.Path"/>.
/// </summary>
public sealed class CanonicalCategoryMapper(ZhuaDbContext db) : ICanonicalCategoryMapper
{
    public async Task<CanonicalCategoryMapResult> MapAsync(CancellationToken ct = default)
    {
        var storeCats = await db.StoreCategories.Include(c => c.Store).ToListAsync(ct);
        var byId = storeCats.ToDictionary(c => c.Id);
        var canonByPath = await db.CanonicalCategories.ToDictionaryAsync(c => c.Path, ct);

        // --- 1) Build the canonical tree from the Foodstuffs taxonomy. Parents (Department) before children
        //        (Aisle, Shelf) so each node's parent canonical already exists. ---
        var foodstuffs = storeCats.Where(c => c.Store.Chain is Chain.NewWorld or Chain.PaknSave).ToList();
        foreach (var kind in (ReadOnlySpan<CategoryKind>)[CategoryKind.Department, CategoryKind.Aisle, CategoryKind.Shelf])
        {
            foreach (var sc in foodstuffs.Where(c => c.Kind == kind))
            {
                var parent = sc.ParentId is { } pid && byId.TryGetValue(pid, out var psc) ? psc.CanonicalCategory : null;
                var slug = Slugify(sc.Name);
                var path = parent is null ? slug : $"{parent.Path}/{slug}";

                if (!canonByPath.TryGetValue(path, out var canon))
                {
                    canon = new CanonicalCategory { Kind = sc.Kind, Name = sc.Name, Slug = slug, Path = path, Parent = parent };
                    db.CanonicalCategories.Add(canon);
                    canonByPath[path] = canon;
                }
                sc.CanonicalCategory = canon;
            }
        }

        // --- 2) Map other banners' store categories in by unambiguous (kind, slug). Unmatched stay null —
        //        canonical PRODUCTS are categorised from their Foodstuffs member below, so this isn't blocking. ---
        var canonBySlugKind = canonByPath.Values
            .GroupBy(c => (c.Kind, c.Slug))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var sc in storeCats.Where(c => c.Store.Chain is not (Chain.NewWorld or Chain.PaknSave)))
        {
            if (sc.CanonicalCategory is not null) continue;
            if (canonBySlugKind.TryGetValue((sc.Kind, Slugify(sc.Name)), out var canon))
                sc.CanonicalCategory = canon;
        }

        // --- 3) Assign each canonical product its finest mapped category (Foodstuffs member preferred). ---
        var products = await db.CanonicalProducts
            .Include(c => c.StoreProducts).ThenInclude(sp => sp.Categories)
            .Include(c => c.StoreProducts).ThenInclude(sp => sp.Store)
            .AsSplitQuery()
            .ToListAsync(ct);

        var categorized = 0;
        foreach (var cp in products)
        {
            var best = FinestMappedCategory(cp);
            if (best is null) continue;
            cp.CanonicalCategory = best;
            cp.Category = best.Name;
            categorized++;
        }

        await db.SaveChangesAsync(ct);

        return new CanonicalCategoryMapResult(
            canonByPath.Count,
            storeCats.Count(c => c.CanonicalCategoryId is not null),
            categorized);

        static CanonicalCategory? FinestMappedCategory(CanonicalProduct cp)
        {
            // Foodstuffs members first (authoritative taxonomy), then any member; finest kind (Shelf) first.
            foreach (var preferFoodstuffs in (ReadOnlySpan<bool>)[true, false])
            {
                foreach (var kind in (ReadOnlySpan<CategoryKind>)[CategoryKind.Shelf, CategoryKind.Aisle, CategoryKind.Department])
                {
                    foreach (var sp in cp.StoreProducts)
                    {
                        if (preferFoodstuffs != (sp.Store.Chain is Chain.NewWorld or Chain.PaknSave)) continue;
                        var c = sp.Categories.FirstOrDefault(x => x.Kind == kind && x.CanonicalCategory is not null);
                        if (c is not null) return c.CanonicalCategory;
                    }
                }
            }
            return null;
        }
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        var prevDash = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); prevDash = false; }
            else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
        }
        return sb.ToString().Trim('-');
    }
}
