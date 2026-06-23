using Zhua.Domain.Entities;
using Zhua.Domain.ValueObjects;

namespace Zhua.Ingestion.Tests;

/// <summary>Pure Domain tests for the change-only price rule (plan D3) now owned by the entity (D19) — no EF.</summary>
public class StoreProductTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-23T06:00:00Z");

    private static StoreProduct NewProduct() => new() { SourceSku = "SKU", RawName = "x", FirstSeenAt = T0 };

    private static StoreProductObservation Milk(decimal price, bool onSpecial = false, decimal? nonSpecial = null) =>
        new("Anchor Blue Milk 2L", "Anchor", "2L", "9400000000001", null, null,
            price, nonSpecial, onSpecial, price / 2, "1L");

    [Fact]
    public void First_observation_counts_as_a_change_and_sets_fields()
    {
        var p = NewProduct();

        var snap = p.ApplyObservation(Milk(3.50m), T0);

        Assert.NotNull(snap);
        Assert.Single(p.PriceSnapshots);
        Assert.Equal(3.50m, p.CurrentPrice);
        Assert.Equal("Anchor Blue Milk 2L", p.RawName);
        Assert.Equal(T0, p.LastSeenAt);
        Assert.Equal(T0, p.PriceUpdatedAt);
    }

    [Fact]
    public void Unchanged_price_returns_null_but_still_refreshes_lastseen()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);

        var later = T0.AddHours(12);
        var snap = p.ApplyObservation(Milk(3.50m), later);

        Assert.Null(snap);
        Assert.Single(p.PriceSnapshots);          // no new snapshot
        Assert.Equal(later, p.LastSeenAt);         // liveness still refreshed (R4)
        Assert.Equal(T0, p.PriceUpdatedAt);        // price-change time unchanged
    }

    [Fact]
    public void Changed_price_appends_a_snapshot()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);

        var snap = p.ApplyObservation(Milk(3.20m), T0.AddHours(12));

        Assert.NotNull(snap);
        Assert.Equal(2, p.PriceSnapshots.Count);
        Assert.Equal(3.20m, p.CurrentPrice);
    }

    [Fact]
    public void Going_on_special_at_the_same_price_is_a_change()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);

        var snap = p.ApplyObservation(Milk(3.50m, onSpecial: true, nonSpecial: 4.00m), T0.AddHours(12));

        Assert.NotNull(snap);
        Assert.True(p.IsOnSpecial);
        Assert.Equal(4.00m, p.CurrentNonSpecialPrice);
        Assert.Equal(2, p.PriceSnapshots.Count);
    }
}
