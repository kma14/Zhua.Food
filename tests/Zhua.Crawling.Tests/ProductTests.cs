using Zhua.Domain.Entities;
using Zhua.Domain.ValueObjects;

namespace Zhua.Crawling.Tests;

/// <summary>Pure Domain tests for the change-only price rule (plan D3) now owned by the entity (D19) — no EF.</summary>
public class ProductTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-23T06:00:00Z");

    private static Product NewProduct() => new() { SourceSku = "SKU", RawName = "x", FirstSeenAt = T0 };

    private static ProductObservation Milk(decimal price, bool onSpecial = false, decimal? nonSpecial = null) =>
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

    // --- Foodstuffs was-price reconstruction: source flags a special but publishes no was-price (D13 gap). ---

    [Fact]
    public void Foodstuffs_special_reconstructs_was_price_from_prior_shelf_price()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);                                    // regular shelf price seen

        var snap = p.ApplyObservation(Milk(2.99m, onSpecial: true), T0.AddHours(12)); // on special, no was-price

        Assert.NotNull(snap);
        Assert.True(p.IsOnSpecial);
        Assert.Equal(3.50m, p.CurrentNonSpecialPrice);   // recovered from the prior shelf price
        Assert.Equal(3.50m, snap!.NonSpecialPrice);      // and recorded into history
    }

    [Fact]
    public void Reconstructed_was_price_carries_forward_while_special_holds()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);
        p.ApplyObservation(Milk(2.99m, onSpecial: true), T0.AddHours(12));      // reconstructs was = 3.50

        var snap = p.ApplyObservation(Milk(2.99m, onSpecial: true), T0.AddHours(24)); // still on special, same price

        Assert.Null(snap);                               // nothing changed → no spurious snapshot
        Assert.Equal(3.50m, p.CurrentNonSpecialPrice);   // carried forward, not lost
    }

    [Fact]
    public void First_sighting_already_on_special_leaves_was_price_null()
    {
        var p = NewProduct();

        var snap = p.ApplyObservation(Milk(2.99m, onSpecial: true), T0);        // no prior state to recover from

        Assert.NotNull(snap);
        Assert.Null(p.CurrentNonSpecialPrice);           // unrecoverable — going-forward only
    }

    [Fact]
    public void Special_priced_at_or_above_prior_does_not_fabricate_a_saving()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);

        p.ApplyObservation(Milk(3.50m, onSpecial: true), T0.AddHours(12));      // "special" not actually cheaper

        Assert.Null(p.CurrentNonSpecialPrice);           // guard: prior must be strictly higher
    }
}
