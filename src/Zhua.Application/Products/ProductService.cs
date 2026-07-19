using Zhua.Application.Categories;
using Zhua.Application.Common;
using Zhua.Application.Pricing;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Products;

/// <summary>
/// The store-first grouped product collection + the admin item-link write (D27). All logic — category-subtree
/// resolution, grouping by item, comparable unit-price, ordering, paging, link orchestration — lives here over the
/// repository ports; no EF. Groups the real per-store listings by item and computes no group aggregates (client ranks).
/// </summary>
public sealed class ProductService(
    IProductRepository products,
    ICategoryRepository categories,
    IMatchCandidateRepository candidates,
    IUnitOfWork uow) : IProductService
{
    public async Task<PagedResult<ProductGroup>?> ListAsync(
        string? q, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size, string? sort)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);
        var appliedSort = NormalizeSort(sort);

        // Resolve the category subtree (if filtering by category) via the shared resolver. Archived nodes are hidden
        // (D25 phase 3); an unknown/archived id → null → 404.
        IReadOnlyCollection<Guid>? subtree = null;
        if (categoryId is { } catId)
        {
            subtree = CategorySubtree.Resolve(await categories.GetActiveAsync(), catId);
            if (subtree is null) return null;
        }

        // The storeId filter is applied to listings in the repository, so each built group already contains only the
        // visible listings and groups with none don't exist — total/sort/paging are all computed post-filter.
        var listings = await products.FindListingsAsync(q, subtree, storeIds);
        var sorted = SortGroups(Build(listings), appliedSort);   // sort the whole set BEFORE paging → correct pages

        var total = sorted.Count;
        var totalPages = (int)Math.Ceiling(total / (double)size);
        var items = sorted.Skip((page - 1) * size).Take(size).ToList();
        return new PagedResult<ProductGroup>(items, page, size, total, totalPages, page < totalPages, appliedSort);
    }

    public async Task<ProductGroup?> GetGroupAsync(Guid productId)
    {
        var product = await products.GetByIdAsync(productId);
        if (product is null) return null;
        var listings = await products.FindGroupAsync(product.ItemId, productId);
        return Build(listings).FirstOrDefault();
    }

    public async Task<ProductPriceHistory?> GetPriceHistoryAsync(Guid productId, int? days)
    {
        var product = await products.GetByIdAsync(productId);
        if (product is null) return null;

        var since = days is > 0 ? DateTimeOffset.UtcNow.AddDays(-days.Value) : DateTimeOffset.MinValue;
        var group = await products.FindGroupWithHistoryAsync(product.ItemId, productId, since);

        var stores = group
            .Select(sp => new
            {
                sp.Store.Name, sp.Store.Chain, sp.Store.Suburb,
                Points = sp.PriceSnapshots
                    .OrderBy(ps => ps.CapturedAt)
                    .Select(ps => new PriceHistoryPoint(
                        ps.CapturedAt, ps.Price, ps.IsOnSpecial, ps.NonSpecialPrice,
                        PromoTypeLabel(ps.PromoType), ps.MemberPrice, ps.UnitPrice))
                    .ToList(),
            })
            .Where(r => r.Points.Count > 0)
            .OrderBy(r => r.Name)
            .Select(r => new StorePriceHistory(r.Name, r.Chain.ToString(), r.Suburb, r.Points))
            .ToList();

        return new ProductPriceHistory(productId, product.RawName, product.RawBrand, product.RawSize, stores);
    }

    public async Task<Result<ProductLinkView>> LinkAsync(Guid productId, Guid? itemId)
    {
        var product = await products.GetForUpdateAsync(productId);
        if (product is null) return Result<ProductLinkView>.NotFound("product not found");

        if (itemId is { } id)
        {
            // Reject a merged-away item (rework phase 4): linking to a redirect tombstone would be undone next run.
            if (!await products.IsLinkableItemAsync(id))
                return Result<ProductLinkView>.NotFound("item not found");

            product.ItemId = id;
            foreach (var c in await candidates.GetPendingByProductAsync(productId))
                candidates.Remove(c);   // this listing is resolved now
        }
        else
        {
            product.ItemId = null;      // unlink
        }

        await uow.SaveChangesAsync();
        return Result<ProductLinkView>.Ok(new ProductLinkView(product.Id, product.ItemId));
    }

    /// <summary>Group the listings by item and build each <see cref="ProductGroup"/> (no ordering/paging) — pure.</summary>
    private static List<ProductGroup> Build(IReadOnlyList<Product> listings) =>
        listings
            .GroupBy(p => p.ItemId ?? p.Id)        // matched listings collapse; unmatched = a group of one
            .Select(g =>
            {
                var any = g.First();
                var products = g
                    .OrderBy(p => p.CurrentPrice)   // a neutral default order; the client re-sorts as it likes
                    .Select(p =>
                    {
                        var norm = UnitPriceNormalizer.ToComparable(p.UnitPrice, p.UnitOfMeasure);
                        return new ProductListing(
                            p.Id, p.Sku, p.Store.Name, p.Store.Chain.ToString(), p.Store.Suburb, p.RawName, p.RawBrand, p.RawSize,
                            p.ImageUrl, p.CurrentPrice, p.IsOnSpecial, p.CurrentNonSpecialPrice,
                            PromoTypeLabel(p.PromoType), p.MemberPrice, p.MultibuyQuantity, p.MultibuyTotal,
                            norm is { } n ? decimal.Round(n.Price, 2) : null, norm?.Unit,
                            p.PriceUpdatedAt, p.LastSeenAt);
                    })
                    .ToList();
                return new ProductGroup(any.ItemId, any.Item?.Description, any.Item?.Category, products);
            })
            .ToList();

    // ---- Server-side sort (applied over the whole filtered set, before paging) ----------------------------------
    private const string SortUnitPriceAsc = "unitPriceAsc";   // default
    private const string SortPriceAsc = "priceAsc";
    private const string SortNameAsc = "nameAsc";
    private const string SortDiscountDesc = "discountDesc";

    /// <summary>Enum → API label ("Special" | "MemberPrice" | "Multibuy"); None serialises as null.</summary>
    internal static string? PromoTypeLabel(PromoType t) => t == PromoType.None ? null : t.ToString();

    /// <summary>Map the raw sort param to a supported key; unknown/blank/null ⇒ the default (echoed back).</summary>
    private static string NormalizeSort(string? sort) => sort?.Trim() switch
    {
        SortPriceAsc => SortPriceAsc,
        SortNameAsc => SortNameAsc,
        SortDiscountDesc => SortDiscountDesc,
        _ => SortUnitPriceAsc,
    };

    /// <summary>
    /// Order the groups by the chosen key. Keys derive from each group's (already store-filtered) listings; for the
    /// ascending price keys a null value sorts last; ties break by name so the order is stable across pages.
    /// </summary>
    private static List<ProductGroup> SortGroups(List<ProductGroup> groups, string sort) => sort switch
    {
        SortPriceAsc => groups
            .OrderBy(g => MinOrNull(g, l => l.Price) ?? decimal.MaxValue)
            .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase).ToList(),
        SortNameAsc => groups
            .OrderBy(NameKey, StringComparer.OrdinalIgnoreCase).ToList(),
        SortDiscountDesc => groups
            .OrderByDescending(MaxSaving)
            .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase).ToList(),
        _ /* unitPriceAsc: lowest comparable unit price, fall back to shelf price */ => groups
            .OrderBy(g => (MinOrNull(g, l => l.UnitPrice) ?? MinOrNull(g, l => l.Price)) ?? decimal.MaxValue)
            .ThenBy(NameKey, StringComparer.OrdinalIgnoreCase).ToList(),
    };

    /// <summary>The lowest non-null value across a group's listings, or null if every listing is null.</summary>
    private static decimal? MinOrNull(ProductGroup g, Func<ProductListing, decimal?> pick)
    {
        decimal? min = null;
        foreach (var l in g.Products)
            if (pick(l) is { } v && (min is null || v < min)) min = v;
        return min;
    }

    /// <summary>The biggest current saving (wasPrice − price) among the group's on-special listings; 0 if none.</summary>
    private static decimal MaxSaving(ProductGroup g)
    {
        decimal max = 0;
        foreach (var l in g.Products)
            if (l.WasPrice is { } was && l.Price is { } price && was > price && was - price > max)
                max = was - price;
        return max;
    }

    private static string NameKey(ProductGroup g) => g.Description ?? g.Products[0].Name;
}
