using Microsoft.EntityFrameworkCore;
using Zhua.Application.Crawling;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.ValueObjects;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Crawling;

/// <summary>
/// Runs one crawl for a store: open <see cref="CrawlRun"/> → fetch → for each product apply the observation
/// (the change-only price rule, D3, lives on <see cref="Product.ApplyObservation"/>) → link categories
/// (D11) → sync promo tags (D13) → reconcile products the crawl did NOT return (D28, complete runs only)
/// → close the run. This type is the use-case orchestration; the per-product invariant is owned by the
/// entity (D19).
/// </summary>
public sealed class CrawlOrchestrator(
    ZhuaDbContext db,
    IEnumerable<IStoreCrawler> crawlers,
    TimeProvider clock) : ICrawlOrchestrator
{
    public async Task<CrawlRunResult> RunAsync(Guid storeId, CancellationToken ct = default)
    {
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == storeId, ct)
            ?? throw new InvalidOperationException($"Store {storeId} not found.");

        var run = new CrawlRun { StoreId = store.Id, StartedAt = clock.GetUtcNow(), Status = CrawlRunStatus.Running };
        db.CrawlRuns.Add(run);
        await db.SaveChangesAsync(ct); // assign run.Id

        try
        {
            var crawler = crawlers.FirstOrDefault(c => c.Chain == store.Chain)
                ?? throw new InvalidOperationException($"No crawler registered for chain {store.Chain}.");

            var fetched = await crawler.FetchAsync(store, ct);
            var scraped = fetched.Products;

            var existing = await db.Products
                .Include(p => p.Categories)
                .Include(p => p.Tags)
                .AsSplitQuery() // two collection includes — split to avoid a cartesian-explosion warning
                .Where(p => p.StoreId == store.Id)
                .ToDictionaryAsync(p => p.Sku, ct);

            var categories = await db.StoreCategories
                .Where(c => c.StoreId == store.Id)
                .ToDictionaryAsync(c => (c.Kind, c.ExternalId), ct);

            // Tag dimension is chain-scoped (plan D13).
            var tags = await db.ProductTags
                .Where(t => t.Chain == store.Chain)
                .ToDictionaryAsync(t => (t.Source, t.Code), ct);

            // Tags are volatile → reset each product's set the first time we see it this run (D13).
            var tagsResetForSku = new HashSet<string>();

            // Category links accumulate WITHIN a run (a product legitimately sits under several shelves — D11), but
            // must NOT accumulate ACROSS runs: a product Woolworths re-shelves (or a WAF-cooldown response
            // mis-attributes) would otherwise keep its stale links forever, and CategoryMapper's finest-first pick
            // could then land on a wrong one (a colby cheese categorised as "Barn Eggs"). So reset the set the first
            // time we see the product this run — but ONLY on a complete crawl: a partial one didn't query every
            // aisle, so "not linked this run" says nothing, and clearing would drop legitimately-absent shelves
            // (mirrors the D28 missing-product guard below). Partial run → null → no reset, links just accumulate.
            var categoriesResetForSku = fetched.IsComplete ? new HashSet<string>() : null;

            var now = clock.GetUtcNow();
            var snapshotsWritten = 0;

            foreach (var s in scraped)
            {
                if (!existing.TryGetValue(s.Sku, out var sp))
                {
                    sp = new Product
                    {
                        StoreId = store.Id,
                        Sku = s.Sku,
                        RawName = s.Name,
                        FirstSeenAt = now,
                    };
                    db.Products.Add(sp);
                    existing[s.Sku] = sp;
                }

                // D3/R4 price rule is owned by the entity; we just link the snapshot to this run.
                var snapshot = sp.ApplyObservation(ToObservation(s), now);
                if (snapshot is not null)
                {
                    snapshot.CrawlRun = run;
                    snapshotsWritten++;
                }

                if (categoriesResetForSku?.Add(s.Sku) == true)
                    sp.Categories.Clear();               // drop last run's links; re-add this run's below (D11/D28)
                LinkCategories(sp, s.CategoryPath, store.Id, categories);

                if (tagsResetForSku.Add(s.Sku))
                    sp.Tags.Clear();
                SyncTags(sp, s.Tags, store.Chain, tags);
            }

            // Missing-product reconciliation (plan D28) — ONLY when the crawler claims full coverage: after a
            // partial scrape "not scraped" says nothing about the shelf, and counting it would let one lost
            // page/department retire (and de-special) half a store.
            if (fetched.IsComplete)
            {
                var seen = scraped.Select(s => s.Sku).ToHashSet();
                foreach (var (sku, product) in existing)
                    if (!seen.Contains(sku))
                        product.RecordMissing(now);
            }

            run.ProductsFound = scraped.Count;
            run.SnapshotsWritten = snapshotsWritten;
            run.Status = fetched.IsComplete ? CrawlRunStatus.Succeeded : CrawlRunStatus.Partial;
            run.ErrorMessage = fetched.IsComplete ? null : Truncate(string.Join(" | ", fetched.Gaps), MaxErrorLength);
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);

            return new CrawlRunResult(run.Id, run.Status, run.ProductsFound, run.SnapshotsWritten, run.ErrorMessage);
        }
        catch (Exception ex)
        {
            run.Status = CrawlRunStatus.Failed;
            run.FinishedAt = clock.GetUtcNow();
            // Truncate: Playwright exceptions carry a full call-log (well over the ErrorMessage column's
            // varchar(2000)); storing it raw makes this very failure-record save throw 22001 and orphans the run.
            run.ErrorMessage = Truncate(ex.Message, MaxErrorLength);
            await db.SaveChangesAsync(CancellationToken.None);
            return new CrawlRunResult(run.Id, run.Status, run.ProductsFound, run.SnapshotsWritten, ex.Message);
        }
    }

    /// <summary>Max persisted <see cref="CrawlRun.ErrorMessage"/> length — matches CrawlRunConfiguration's HasMaxLength.</summary>
    private const int MaxErrorLength = 2000;

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static ProductObservation ToObservation(ScrapedProduct s) => new(
        s.Name, s.Brand, s.Size, s.Gtin, s.Url, s.ImageUrl,
        s.Price, s.NonSpecialPrice, s.PromoType, s.MemberPrice, s.MultibuyQuantity, s.MultibuyTotal,
        s.UnitPrice, s.UnitOfMeasure);

    /// <summary>Upserts the product's Department→Aisle→Shelf nodes and links them many-to-many (plan D11).</summary>
    private void LinkCategories(
        Product sp, IReadOnlyList<ScrapedCategoryNode> path, Guid storeId,
        Dictionary<(CategoryKind Kind, string ExternalId), StoreCategory> categories)
    {
        StoreCategory? parent = null;
        foreach (var node in path)
        {
            var key = (node.Kind, node.ExternalId);
            if (!categories.TryGetValue(key, out var cat))
            {
                cat = new StoreCategory
                {
                    StoreId = storeId,
                    Kind = node.Kind,
                    ExternalId = node.ExternalId,
                    Slug = node.Slug,
                    Name = node.Name,
                    Parent = parent,
                };
                db.StoreCategories.Add(cat);
                categories[key] = cat;
            }
            else if (cat.ParentId is null && cat.Parent is null && parent is not null)
            {
                cat.Parent = parent;
            }

            if (!sp.Categories.Any(c => c.Kind == cat.Kind && c.ExternalId == cat.ExternalId))
                sp.Categories.Add(cat);

            parent = cat;
        }
    }

    /// <summary>Adds the crawl's promo tags, upserting the chain-scoped tag dimension (plan D13). Reset happens in the caller.</summary>
    private void SyncTags(
        Product sp, IReadOnlyList<ScrapedTag> scrapedTags, Chain chain,
        Dictionary<(ProductTagSource Source, string Code), ProductTag> tags)
    {
        foreach (var t in scrapedTags)
        {
            var key = (t.Source, t.Code);
            if (!tags.TryGetValue(key, out var tag))
            {
                tag = new ProductTag { Chain = chain, Source = t.Source, Code = t.Code, Label = t.Label };
                db.ProductTags.Add(tag);
                tags[key] = tag;
            }

            if (!sp.Tags.Any(x => x.Source == tag.Source && x.Code == tag.Code))
                sp.Tags.Add(tag);
        }
    }
}
