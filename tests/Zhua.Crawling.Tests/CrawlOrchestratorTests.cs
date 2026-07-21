using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Zhua.Application.Crawling;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Crawling;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Crawling.Tests;

/// <summary>Proves the change-only snapshot behaviour (plan D3) + liveness refresh (R4).</summary>
public class CrawlOrchestratorTests
{
    private static readonly Guid StoreId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-06-21T06:00:00Z"));

    private DbContextOptions<ZhuaDbContext> Options() =>
        new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseInMemoryDatabase(nameof(CrawlOrchestratorTests), _root)
            // Each test method gets its own isolated InMemoryDatabaseRoot by design — the correct pattern for
            // test isolation, not the production misuse this warning is meant to catch.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

    private ZhuaDbContext NewContext() => new(Options());

    private async Task SeedStoreAsync()
    {
        await using var db = NewContext();
        db.Stores.Add(new Store
        {
            Id = StoreId,
            Chain = Chain.Woolworths,
            Name = "Test Store",
            Suburb = "Test",
            Latitude = -36.78,
            Longitude = 174.76,
        });
        await db.SaveChangesAsync();
    }

    private async Task<CrawlRunResult> RunAsync(params ScrapedProduct[] products)
    {
        await using var db = NewContext();
        var orchestrator = new CrawlOrchestrator(db, [new StubCrawler(Chain.Woolworths, products)], _clock);
        return await orchestrator.RunAsync(StoreId);
    }

    private async Task<CrawlRunResult> RunPartialAsync(params ScrapedProduct[] products)
    {
        await using var db = NewContext();
        var orchestrator = new CrawlOrchestrator(
            db, [new StubCrawler(Chain.Woolworths, products, gaps: ["frozen: page 3 of 16 failed after retries"])], _clock);
        return await orchestrator.RunAsync(StoreId);
    }

    private static ScrapedProduct Milk(decimal price, bool onSpecial = false, decimal? nonSpecial = null) => new()
    {
        Sku = "SKU-MILK",
        Name = "Anchor Blue Milk 2L",
        Price = price,
        PromoType = onSpecial ? PromoType.Special : PromoType.None,
        NonSpecialPrice = nonSpecial,
    };

    [Fact]
    public async Task First_crawl_creates_product_and_one_snapshot()
    {
        await SeedStoreAsync();

        var result = await RunAsync(Milk(3.50m));

        Assert.Equal(CrawlRunStatus.Succeeded, result.Status);
        Assert.Equal(1, result.ProductsFound);
        Assert.Equal(1, result.SnapshotsWritten);

        await using var db = NewContext();
        Assert.Equal(1, await db.Products.CountAsync());
        Assert.Equal(1, await db.PriceSnapshots.CountAsync());
        Assert.Equal(3.50m, (await db.Products.SingleAsync()).CurrentPrice);
    }

    [Fact]
    public async Task Unchanged_price_writes_no_new_snapshot_but_refreshes_lastseen()
    {
        await SeedStoreAsync();
        await RunAsync(Milk(3.50m));

        _clock.Advance(TimeSpan.FromHours(12));
        var result = await RunAsync(Milk(3.50m));

        Assert.Equal(0, result.SnapshotsWritten);

        await using var db = NewContext();
        Assert.Equal(1, await db.PriceSnapshots.CountAsync());
        Assert.Equal(_clock.GetUtcNow(), (await db.Products.SingleAsync()).LastSeenAt);
    }

    [Fact]
    public async Task Changed_price_writes_a_new_snapshot()
    {
        await SeedStoreAsync();
        await RunAsync(Milk(3.50m));

        _clock.Advance(TimeSpan.FromHours(12));
        var result = await RunAsync(Milk(3.20m));

        Assert.Equal(1, result.SnapshotsWritten);

        await using var db = NewContext();
        Assert.Equal(2, await db.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Going_on_special_at_same_price_counts_as_a_change()
    {
        await SeedStoreAsync();
        await RunAsync(Milk(3.50m));

        _clock.Advance(TimeSpan.FromHours(12));
        var result = await RunAsync(Milk(3.50m, onSpecial: true, nonSpecial: 4.00m));

        Assert.Equal(1, result.SnapshotsWritten);

        await using var db = NewContext();
        Assert.Equal(2, await db.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Tags_are_reset_each_crawl()
    {
        await SeedStoreAsync();

        // First crawl: on special.
        await RunAsync(Milk(3.50m, onSpecial: true, nonSpecial: 4.00m) with
        {
            Tags = [new ScrapedTag(ProductTagSource.Primary, "IsSpecial")],
        });

        // Next crawl: special ended, now an everyday "Low Price".
        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Milk(3.50m) with
        {
            Tags = [new ScrapedTag(ProductTagSource.Primary, "IsGreatPrice")],
        });

        await using var db = NewContext();
        var sp = await db.Products.Include(p => p.Tags).SingleAsync();
        Assert.Single(sp.Tags);                                  // stale IsSpecial dropped
        Assert.Equal("IsGreatPrice", sp.Tags.Single().Code);
        Assert.Equal(2, await db.ProductTags.CountAsync());      // both rows kept in the shared dimension
    }

    // ---- Missing-product reconciliation (plan D28) --------------------------------------------------------------

    private static ScrapedProduct Bread(decimal price = 2.00m) => new()
    {
        Sku = "SKU-BREAD", Name = "Toast Bread 700g", Price = price, PromoType = PromoType.None,
    };

    [Fact]
    public async Task Product_missing_from_one_complete_run_starts_a_streak_but_stays_available()
    {
        await SeedStoreAsync();
        await RunAsync(Milk(3.50m, onSpecial: true, nonSpecial: 4.00m), Bread());

        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Bread()); // milk vanished from a COMPLETE crawl

        await using var db = NewContext();
        var milk = await db.Products.SingleAsync(p => p.Sku == "SKU-MILK");
        Assert.True(milk.IsAvailable);                       // one miss is not a delisting
        Assert.True(milk.IsOnSpecial);                       // promo untouched until retired
        Assert.Equal(1, milk.ConsecutiveMissingRuns);
        Assert.Equal(_clock.GetUtcNow(), milk.MissingSince);
    }

    [Fact]
    public async Task Product_missing_from_two_complete_runs_is_retired_with_promo_cleared()
    {
        // The stale-special bug: Highland Park's Round Green Bean froze at its 2026-07-13 special after the
        // store delisted it, and /deals served it indefinitely.
        await SeedStoreAsync();
        await RunAsync(Milk(4.99m, onSpecial: true, nonSpecial: 22.99m), Bread());

        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Bread());
        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Bread());

        await using var db = NewContext();
        var milk = await db.Products.SingleAsync(p => p.Sku == "SKU-MILK");
        Assert.False(milk.IsAvailable);
        Assert.False(milk.IsOnSpecial);
        Assert.Equal(PromoType.None, milk.PromoType);
        Assert.Null(milk.CurrentNonSpecialPrice);
        Assert.Equal(4.99m, milk.CurrentPrice);              // last-known price kept for display/history
        Assert.Equal(2, milk.ConsecutiveMissingRuns);
        // Deliberately no synthetic snapshot — history keeps only real observations (D28).
        Assert.Equal(1, await db.PriceSnapshots.CountAsync(s => s.Product.Sku == "SKU-MILK"));
    }

    [Fact]
    public async Task Partial_run_is_recorded_as_partial_and_does_not_count_products_missing()
    {
        await SeedStoreAsync();
        await RunAsync(Milk(3.50m), Bread());

        _clock.Advance(TimeSpan.FromHours(12));
        var result = await RunPartialAsync(Bread()); // milk missing, but the scrape had a gap

        Assert.Equal(CrawlRunStatus.Partial, result.Status);
        Assert.Contains("frozen", result.Error);             // the gap summary lands on the run

        await using var db = NewContext();
        var milk = await db.Products.SingleAsync(p => p.Sku == "SKU-MILK");
        Assert.True(milk.IsAvailable);
        Assert.Equal(0, milk.ConsecutiveMissingRuns);        // incomplete coverage proves nothing
        Assert.Null(milk.MissingSince);
    }

    [Fact]
    public async Task Reappearing_product_becomes_available_again_and_resets_the_streak()
    {
        await SeedStoreAsync();
        await RunAsync(Milk(3.50m), Bread());

        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Bread());
        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Bread());                             // retired after two misses

        _clock.Advance(TimeSpan.FromHours(12));
        await RunAsync(Milk(3.60m), Bread());                // back on the shelf

        await using var db = NewContext();
        var milk = await db.Products.SingleAsync(p => p.Sku == "SKU-MILK");
        Assert.True(milk.IsAvailable);
        Assert.Equal(0, milk.ConsecutiveMissingRuns);
        Assert.Null(milk.MissingSince);
        Assert.Equal(3.60m, milk.CurrentPrice);
        Assert.Equal(_clock.GetUtcNow(), milk.LastSeenAt);
    }

    [Fact]
    public async Task Missing_crawler_records_a_failed_run()
    {
        await SeedStoreAsync();
        await using var db = NewContext();

        var orchestrator = new CrawlOrchestrator(db, [], _clock); // no crawler registered
        var result = await orchestrator.RunAsync(StoreId);

        Assert.Equal(CrawlRunStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(1, await db.CrawlRuns.CountAsync(r => r.Status == CrawlRunStatus.Failed));
    }

    [Fact]
    public async Task Crawler_failure_with_huge_message_records_a_truncated_error_and_does_not_throw()
    {
        // Regression: a Playwright launch error carries a >2000-char call-log. Storing it raw made the
        // Failed-status save overflow ErrorMessage's varchar(2000) and crash the whole run (orphaned "Running").
        await SeedStoreAsync();
        await using var db = NewContext();
        var hugeMessage = new string('x', 5000);

        var orchestrator = new CrawlOrchestrator(db, [new ThrowingCrawler(Chain.Woolworths, hugeMessage)], _clock);
        var result = await orchestrator.RunAsync(StoreId); // must not throw

        Assert.Equal(CrawlRunStatus.Failed, result.Status);
        var run = await db.CrawlRuns.SingleAsync();
        Assert.Equal(CrawlRunStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
        Assert.True(run.ErrorMessage!.Length <= 2000, "ErrorMessage must be truncated to fit the column");
    }
}

internal sealed class StubCrawler(Chain chain, IReadOnlyList<ScrapedProduct> products, IReadOnlyList<string>? gaps = null) : IStoreCrawler
{
    public Chain Chain => chain;

    public Task<ScrapeResult> FetchAsync(Store store, CancellationToken ct = default)
        => Task.FromResult(new ScrapeResult(products, gaps ?? []));
}

internal sealed class ThrowingCrawler(Chain chain, string message) : IStoreCrawler
{
    public Chain Chain => chain;

    public Task<ScrapeResult> FetchAsync(Store store, CancellationToken ct = default)
        => throw new InvalidOperationException(message);
}

internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}
