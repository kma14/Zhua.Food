using Microsoft.EntityFrameworkCore;
using Zhua.Application.Ingestion;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Ingestion;

/// <summary>
/// Runs one crawl for a store: open <see cref="CrawlRun"/> → fetch → upsert <see cref="StoreProduct"/>
/// (refresh current price + LastSeenAt every crawl, plan R4) → append a <see cref="PriceSnapshot"/>
/// ONLY when the price tuple changed (plan D3) → close the run.
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

            var scraped = await crawler.FetchAsync(store, ct);

            var existing = await db.StoreProducts
                .Where(p => p.StoreId == store.Id)
                .ToDictionaryAsync(p => p.SourceSku, ct);

            var now = clock.GetUtcNow();
            var snapshotsWritten = 0;

            foreach (var s in scraped)
            {
                if (!existing.TryGetValue(s.SourceSku, out var sp))
                {
                    sp = new StoreProduct
                    {
                        StoreId = store.Id,
                        SourceSku = s.SourceSku,
                        RawName = s.Name,
                        FirstSeenAt = now,
                    };
                    db.StoreProducts.Add(sp);
                    existing[s.SourceSku] = sp;
                }

                // Price tuple per plan D3 — null current price (new product) counts as a change.
                var priceChanged =
                    sp.CurrentPrice != s.Price
                    || sp.IsOnSpecial != s.IsOnSpecial
                    || sp.CurrentNonSpecialPrice != s.NonSpecialPrice
                    || sp.UnitPrice != s.UnitPrice;

                // Always refresh raw fields + denormalized current price + liveness (plan R4 / D3).
                sp.RawName = s.Name;
                sp.RawBrand = s.Brand;
                sp.RawSize = s.Size;
                sp.Gtin = s.Gtin;
                sp.Url = s.Url;
                sp.ImageUrl = s.ImageUrl;
                sp.CurrentPrice = s.Price;
                sp.CurrentNonSpecialPrice = s.NonSpecialPrice;
                sp.IsOnSpecial = s.IsOnSpecial;
                sp.UnitPrice = s.UnitPrice;
                sp.UnitOfMeasure = s.UnitOfMeasure;
                sp.LastSeenAt = now;

                if (priceChanged)
                {
                    sp.PriceUpdatedAt = now;
                    sp.PriceSnapshots.Add(new PriceSnapshot
                    {
                        CrawlRun = run,
                        Price = s.Price,
                        NonSpecialPrice = s.NonSpecialPrice,
                        IsOnSpecial = s.IsOnSpecial,
                        UnitPrice = s.UnitPrice,
                        CapturedAt = now,
                    });
                    snapshotsWritten++;
                }
            }

            run.ProductsFound = scraped.Count;
            run.SnapshotsWritten = snapshotsWritten;
            run.Status = CrawlRunStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);

            return new CrawlRunResult(run.Id, run.Status, run.ProductsFound, run.SnapshotsWritten, null);
        }
        catch (Exception ex)
        {
            run.Status = CrawlRunStatus.Failed;
            run.FinishedAt = clock.GetUtcNow();
            run.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            return new CrawlRunResult(run.Id, run.Status, run.ProductsFound, run.SnapshotsWritten, ex.Message);
        }
    }
}
