using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Matching;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Ingestion.Tests;

/// <summary>Proves the two-tier matcher (plan D9/D18): Foodstuffs auto-group, Woolworths auto-link vs review.</summary>
public class CanonicalMatcherTests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-06-23T00:00:00Z"));

    private static readonly Guid Woolworths = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid NewWorld = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid PaknSave = Guid.Parse("33333333-0000-0000-0000-000000000003");

    private ZhuaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseInMemoryDatabase(nameof(CanonicalMatcherTests), _root).Options);

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Stores.AddRange(
            new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true },
            new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
            new Store { Id = PaknSave, Chain = Chain.PaknSave, Name = "PAK", Suburb = "x", IsActive = true });

        db.StoreProducts.AddRange(
            // Foodstuffs share productId "C1" → one canonical, both linked (Tier 1).
            Sp(NewWorld, "C1", "Smooth & Creamy Colby Cheese", "Mainland", "500g", 10.49m),
            Sp(PaknSave, "C1", "Smooth & Creamy Colby Cheese", "Mainland", "500g", 8.99m),
            // Two Anchor cottage-cheese variants — the Woolworths name can't disambiguate them.
            Sp(NewWorld, "C2", "Chives Cottage Cheese", "Anchor", "250g", 4.50m),
            Sp(NewWorld, "C3", "Original Cottage Cheese", "Anchor", "250g", 4.50m),
            // Woolworths: clear single match → auto-link.
            Sp(Woolworths, "W1", "mainland cheese colby", "Mainland", "500g", 9.40m),
            // Woolworths: ambiguous (matches C2 and C3 equally) → review queue.
            Sp(Woolworths, "W2", "anchor cottage cheese", "Anchor", "250g", 4.20m));

        await db.SaveChangesAsync();
    }

    private StoreProduct Sp(Guid store, string sku, string name, string brand, string size, decimal price) => new()
    {
        StoreId = store,
        SourceSku = sku,
        RawName = name,
        RawBrand = brand,
        RawSize = size,
        CurrentPrice = price,
        FirstSeenAt = _clock.GetUtcNow(),
        LastSeenAt = _clock.GetUtcNow(),
    };

    [Fact]
    public async Task Groups_foodstuffs_autolinks_clear_woolworths_and_queues_ambiguous()
    {
        await SeedAsync();

        await using (var db = NewContext())
            await new CanonicalMatcher(db, _clock).RunAsync();

        await using var check = NewContext();

        // Tier 1: 3 canonicals (Colby, Chives Cottage, Original Cottage).
        Assert.Equal(3, await check.CanonicalProducts.CountAsync());

        // The Mainland Colby canonical links all three store-products (NW + PAK + auto-linked Woolworths).
        var colby = await check.CanonicalProducts.SingleAsync(c => c.MatchKey == "foodstuffs:C1");
        var linked = await check.StoreProducts.CountAsync(p => p.CanonicalProductId == colby.Id);
        Assert.Equal(3, linked);

        // Ambiguous Woolworths product is NOT linked and produced pending candidates instead.
        var w2 = await check.StoreProducts.SingleAsync(p => p.SourceSku == "W2");
        Assert.Null(w2.CanonicalProductId);
        Assert.Equal(2, await check.MatchCandidates.CountAsync(m => m.StoreProductId == w2.Id && m.Status == MatchStatus.Pending));
    }

    [Fact]
    public async Task Rerun_is_idempotent_and_keeps_one_canonical_per_sku()
    {
        await SeedAsync();

        await using (var db = NewContext()) await new CanonicalMatcher(db, _clock).RunAsync();
        await using (var db = NewContext()) await new CanonicalMatcher(db, _clock).RunAsync();

        await using var check = NewContext();
        Assert.Equal(3, await check.CanonicalProducts.CountAsync()); // not doubled
        // W2 still has exactly its 2 pending candidates (not duplicated on re-run).
        var w2 = await check.StoreProducts.SingleAsync(p => p.SourceSku == "W2");
        Assert.Equal(2, await check.MatchCandidates.CountAsync(m => m.StoreProductId == w2.Id));
    }
}
