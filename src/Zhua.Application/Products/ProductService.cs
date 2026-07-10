using Zhua.Application.Common;
using Zhua.Application.Pricing;
using Zhua.Domain.Entities;
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
    public async Task<IReadOnlyList<ProductGroup>?> ListAsync(
        string? q, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size)
    {
        size = Math.Clamp(size, 1, 100);
        page = Math.Max(page, 1);

        // Resolve the category subtree (if filtering by category). Archived nodes are hidden (D25 phase 3);
        // an unknown/archived id → null → 404.
        IReadOnlyCollection<Guid>? subtree = null;
        if (categoryId is { } catId)
        {
            var cats = await categories.GetActiveAsync();
            if (cats.All(c => c.Id != catId)) return null;
            var childrenByParent = cats.Where(c => c.ParentId != null)
                .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
            var set = new HashSet<Guid>();
            var stack = new Stack<Guid>([catId]);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!set.Add(n)) continue;
                if (childrenByParent.TryGetValue(n, out var ch)) foreach (var c in ch) stack.Push(c);
            }
            subtree = set;
        }

        var listings = await products.FindListingsAsync(q, subtree, storeIds);
        return Build(listings)
            .OrderBy(g => g.Description ?? g.Products[0].Name)   // stable, neutral group order — the client re-sorts
            .Skip((page - 1) * size).Take(size)
            .ToList();
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
                    .Select(ps => new PriceHistoryPoint(ps.CapturedAt, ps.Price, ps.IsOnSpecial, ps.NonSpecialPrice, ps.UnitPrice))
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
                            p.Id, p.SourceSku, p.Store.Name, p.Store.Chain.ToString(), p.Store.Suburb, p.RawName, p.RawBrand, p.RawSize,
                            p.ImageUrl, p.CurrentPrice, p.IsOnSpecial, p.CurrentNonSpecialPrice,
                            norm is { } n ? decimal.Round(n.Price, 2) : null, norm?.Unit,
                            p.PriceUpdatedAt, p.LastSeenAt);
                    })
                    .ToList();
                return new ProductGroup(any.ItemId, any.Item?.Description, any.Item?.Category, products);
            })
            .ToList();
}
