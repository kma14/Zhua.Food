using Microsoft.EntityFrameworkCore;
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

    private static ScrapedProduct Milk(decimal price, bool onSpecial = false, decimal? nonSpecial = null) => new()
    {
        Sku = "SKU-MILK",
        Name = "Anchor Blue Milk 2L",
        Price = price,
        IsOnSpecial = onSpecial,
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

internal sealed class StubCrawler(Chain chain, IReadOnlyList<ScrapedProduct> products) : IStoreCrawler
{
    public Chain Chain => chain;

    public Task<IReadOnlyList<ScrapedProduct>> FetchAsync(Store store, CancellationToken ct = default)
        => Task.FromResult(products);
}

internal sealed class ThrowingCrawler(Chain chain, string message) : IStoreCrawler
{
    public Chain Chain => chain;

    public Task<IReadOnlyList<ScrapedProduct>> FetchAsync(Store store, CancellationToken ct = default)
        => throw new InvalidOperationException(message);
}

internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}
