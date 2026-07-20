using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Crawling;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Crawling.Tests;

/// <summary>The per-run promo-distribution report (docs/internals/promotions-model.md, 2026-07-19).</summary>
public class PromoReportTests
{
    private readonly InMemoryDatabaseRoot _root = new();

    private ZhuaDbContext NewContext() => new(
        new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseInMemoryDatabase(nameof(PromoReportTests), _root)
            .Options);

    private static Product P(Guid storeId, string sku, PromoType promo) => new()
    {
        StoreId = storeId, Sku = sku, RawName = sku, CurrentPrice = 1.00m,
        PromoType = promo, IsOnSpecial = promo == PromoType.Special,
        FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Counts_split_per_chain_with_program_names_and_a_total_row()
    {
        var ww = Guid.NewGuid();
        var nw = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = ww, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", Latitude = 0, Longitude = 0 },
                new Store { Id = nw, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", Latitude = 0, Longitude = 0 },
                new Store { Id = inactive, Chain = Chain.PaknSave, Name = "PAK", Suburb = "x", Latitude = 0, Longitude = 0, IsActive = false });
            db.Products.AddRange(
                P(ww, "w1", PromoType.None), P(ww, "w2", PromoType.Special), P(ww, "w3", PromoType.Multibuy),
                P(nw, "n1", PromoType.MemberPrice), P(nw, "n2", PromoType.MemberPrice), P(nw, "n3", PromoType.None),
                P(inactive, "p1", PromoType.Special));   // inactive store must not count
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var lines = await PromoReport.BuildAsync(read);

        var wwLine = Assert.Single(lines, l => l.StartsWith("Woolworths"));
        Assert.Contains("Everyday Rewards", wwLine);

        var nwLine = Assert.Single(lines, l => l.StartsWith("NewWorld"));
        Assert.Contains("New World Clubcard", nwLine);
        Assert.Matches(@"\s3\s+1\s+0\s+2\s+0\s+67%", nwLine);   // total=3 none=1 special=0 member=2 multibuy=0

        var total = Assert.Single(lines, l => l.StartsWith("TOTAL"));
        Assert.Matches(@"\s6\s+2\s+1\s+2\s+1\s+67%", total);    // 6 products (inactive excluded), 4 promoted
        Assert.DoesNotContain(lines, l => l.StartsWith("PaknSave"));
    }
}
