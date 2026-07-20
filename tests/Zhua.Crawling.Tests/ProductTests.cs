using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.ValueObjects;

namespace Zhua.Crawling.Tests;

/// <summary>Pure Domain tests for the change-only price rule (plan D3) now owned by the entity (D19) — no EF.</summary>
public class ProductTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-23T06:00:00Z");

    private static Product NewProduct() => new() { Sku = "SKU", RawName = "x", FirstSeenAt = T0 };

    private static ProductObservation Milk(
        decimal price, bool onSpecial = false, decimal? nonSpecial = null,
        PromoType promo = PromoType.None, decimal? memberPrice = null, int? multiQty = null, decimal? multiTotal = null) =>
        new("Anchor Blue Milk 2L", "Anchor", "2L", "9400000000001", null, null,
            price, nonSpecial, onSpecial ? PromoType.Special : promo, memberPrice, multiQty, multiTotal,
            price / 2, "1L");

    // ---- Availability reconciliation (plan D28) — pure entity behaviour ----------------------------------------

    [Fact]
    public void RecordMissing_below_threshold_only_tracks_the_streak()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m, onSpecial: true, nonSpecial: 4.00m), T0);

        p.RecordMissing(T0.AddHours(12));

        Assert.True(p.IsAvailable);
        Assert.True(p.IsOnSpecial);                          // promo untouched below the threshold
        Assert.Equal(1, p.ConsecutiveMissingRuns);
        Assert.Equal(T0.AddHours(12), p.MissingSince);
    }

    [Fact]
    public void RecordMissing_at_threshold_retires_the_listing_and_clears_every_promo_field()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(10.00m, promo: PromoType.MemberPrice, memberPrice: 8.50m, multiQty: 3, multiTotal: 25.00m), T0);

        p.RecordMissing(T0.AddHours(12));
        p.RecordMissing(T0.AddHours(24));

        Assert.False(p.IsAvailable);
        Assert.Equal(PromoType.None, p.PromoType);
        Assert.Null(p.MemberPrice);
        Assert.Null(p.MultibuyQuantity);
        Assert.Null(p.MultibuyTotal);
        Assert.Equal(10.00m, p.CurrentPrice);                // last-known price survives
        Assert.Equal(T0.AddHours(12), p.MissingSince);       // streak start, not retirement time
        Assert.Single(p.PriceSnapshots);                     // no synthetic "promo ended" snapshot
    }

    [Fact]
    public void Observation_after_a_missing_streak_resets_availability()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);
        p.RecordMissing(T0.AddHours(12));
        p.RecordMissing(T0.AddHours(24));

        p.ApplyObservation(Milk(3.50m), T0.AddHours(36));

        Assert.True(p.IsAvailable);
        Assert.Equal(0, p.ConsecutiveMissingRuns);
        Assert.Null(p.MissingSince);
    }

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

    // --- Promo-type model (docs/internals/promotions-model.md, B2): type + member/multibuy are in the D3 tuple. ---

    [Fact]
    public void Member_price_is_not_on_special_and_its_change_appends_a_snapshot()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m), T0);

        var snap = p.ApplyObservation(
            Milk(3.50m, promo: PromoType.MemberPrice, memberPrice: 2.99m), T0.AddHours(12));

        Assert.NotNull(snap);                            // member price appearing IS a tuple change
        Assert.False(p.IsOnSpecial);                     // …but not a public special (decision C)
        Assert.Equal(PromoType.MemberPrice, p.PromoType);
        Assert.Equal(2.99m, p.MemberPrice);
        Assert.Equal(3.50m, p.CurrentPrice);             // shelf price untouched
        Assert.Null(p.CurrentNonSpecialPrice);           // no D23 reconstruction for member deals
    }

    [Fact]
    public void Promo_type_flip_at_the_same_price_is_a_change()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(2.99m, promo: PromoType.MemberPrice, memberPrice: 2.50m), T0);

        var snap = p.ApplyObservation(Milk(2.99m, onSpecial: true), T0.AddHours(12));

        Assert.NotNull(snap);                            // member→special at the same shelf price snapshots (B2)
        Assert.Equal(PromoType.Special, snap!.PromoType);
        Assert.Null(p.MemberPrice);
        Assert.True(p.IsOnSpecial);
    }

    [Fact]
    public void Multibuy_pair_changes_append_a_snapshot()
    {
        var p = NewProduct();
        p.ApplyObservation(Milk(3.50m, promo: PromoType.Multibuy, multiQty: 3, multiTotal: 9.00m), T0);

        var snap = p.ApplyObservation(
            Milk(3.50m, promo: PromoType.Multibuy, multiQty: 2, multiTotal: 6.50m), T0.AddHours(12));

        Assert.NotNull(snap);
        Assert.Equal(2, p.MultibuyQuantity);
        Assert.Equal(6.50m, p.MultibuyTotal);
        Assert.False(p.IsOnSpecial);                     // multibuy is not a public special either
    }

    // --- Foodstuffs was-price reconstruction: source flags a special but publishes no was-price (D23). ---

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
