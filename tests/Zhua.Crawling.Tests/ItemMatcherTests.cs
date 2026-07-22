using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Zhua.Application.Common;
using Zhua.Application.Matching;
using Zhua.Application.Review;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Services;
using Zhua.Infrastructure.Persistence;
using Zhua.Infrastructure.Repositories;

namespace Zhua.Crawling.Tests;

/// <summary>Proves the two-tier matcher (plan D9/D18): Foodstuffs auto-group, Woolworths auto-link vs review.</summary>
public class ItemMatcherTests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-06-23T00:00:00Z"));

    private static readonly Guid Woolworths = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid NewWorld = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid PaknSave = Guid.Parse("33333333-0000-0000-0000-000000000003");
    private static readonly Guid FreshChoice = Guid.Parse("44444444-0000-0000-0000-000000000004");

    // The matcher is now an Application use case over the matching repository port + the domain policy.
    private ItemMatcher Matcher(ZhuaDbContext db) =>
        new(new MatchingRepository(db), new HeuristicItemMatchingPolicy(), new UnitOfWork(db), _clock);

    private ZhuaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseInMemoryDatabase(nameof(ItemMatcherTests), _root)
            // Each test method gets its own isolated InMemoryDatabaseRoot by design — the correct pattern for
            // test isolation, not the production misuse this warning is meant to catch.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options);

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Stores.AddRange(
            new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true },
            new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
            new Store { Id = PaknSave, Chain = Chain.PaknSave, Name = "PAK", Suburb = "x", IsActive = true });

        db.Products.AddRange(
            // Foodstuffs share productId "C1" → one item, both linked (Tier 1).
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

    private Product Sp(Guid store, string sku, string name, string brand, string size, decimal price) => new()
    {
        StoreId = store,
        Sku = sku,
        RawName = name,
        RawBrand = brand,
        RawSize = size,
        CurrentPrice = price,
        FirstSeenAt = _clock.GetUtcNow(),
        LastSeenAt = _clock.GetUtcNow(),
    };

    /// <summary>A FreshChoice listing — no RawBrand at the source (D26), mirroring the real crawler.</summary>
    private Product FcSp(string sku, string name, string size, decimal price) => new()
    {
        StoreId = FreshChoice,
        Sku = sku,
        RawName = name,
        RawBrand = null,
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
            await Matcher(db).RunAsync();

        await using var check = NewContext();

        // Tier 1: 3 items (Colby, Chives Cottage, Original Cottage).
        Assert.Equal(3, await check.Items.CountAsync());

        // The Mainland Colby item links all three store-products (NW + PAK + auto-linked Woolworths).
        var colby = await check.Items.SingleAsync(c => c.MatchKey == "foodstuffs:C1");
        var linked = await check.Products.CountAsync(p => p.ItemId == colby.Id);
        Assert.Equal(3, linked);

        // Ambiguous Woolworths product is NOT linked and produced pending candidates instead.
        var w2 = await check.Products.SingleAsync(p => p.Sku == "W2");
        Assert.Null(w2.ItemId);
        Assert.Equal(2, await check.MatchCandidates.CountAsync(m => m.ProductId == w2.Id && m.Status == MatchStatus.Pending));
    }

    [Fact]
    public async Task Description_is_seeded_once_and_never_re_minted_from_store_data()
    {
        await SeedAsync();
        await using (var db = NewContext()) await Matcher(db).RunAsync();

        Guid colbyId;
        await using (var check = NewContext())
        {
            var colby = await check.Items.SingleAsync(c => c.MatchKey == "foodstuffs:C1");
            colbyId = colby.Id;
            Assert.Equal("Smooth & Creamy Colby Cheese", colby.Description); // seeded from the representative listing
        }

        // The store renames its listing; re-running the matcher must NOT overwrite the owned item text (D25).
        await using (var edit = NewContext())
        {
            foreach (var sp in await edit.Products.Where(p => p.Sku == "C1").ToListAsync())
                sp.RawName = "TOTALLY DIFFERENT NAME";
            await edit.SaveChangesAsync();
        }
        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var after = NewContext();
        var unchanged = await after.Items.SingleAsync(c => c.Id == colbyId);
        Assert.Equal("Smooth & Creamy Colby Cheese", unchanged.Description); // owned phrase held
        Assert.Equal("Smooth & Creamy Colby Cheese", unchanged.Name);        // name also no longer re-minted
    }

    [Fact]
    public async Task Rerun_is_idempotent_and_keeps_one_item_per_sku()
    {
        await SeedAsync();

        await using (var db = NewContext()) await Matcher(db).RunAsync();
        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        Assert.Equal(3, await check.Items.CountAsync()); // not doubled
        // W2 still has exactly its 2 pending candidates (not duplicated on re-run).
        var w2 = await check.Products.SingleAsync(p => p.Sku == "W2");
        Assert.Equal(2, await check.MatchCandidates.CountAsync(m => m.ProductId == w2.Id));
    }

    [Fact]
    public async Task Matcher_respects_a_merge_and_does_not_resurrect_the_merged_item()
    {
        await SeedAsync();
        await using (var db = NewContext()) await Matcher(db).RunAsync();

        Guid survivorId, mergedId;
        await using (var check = NewContext())
        {
            survivorId = (await check.Items.SingleAsync(c => c.MatchKey == "foodstuffs:C2")).Id;
            mergedId = (await check.Items.SingleAsync(c => c.MatchKey == "foodstuffs:C3")).Id;
        }

        // Admin decides the two cottage-cheese SKUs are the same product → merge C3 into C2.
        await using (var db = NewContext())
        {
            var itemService = new ItemService(
                new ItemRepository(db), new ProductRepository(db), new MatchCandidateRepository(db), new UnitOfWork(db));
            Assert.Equal(ResultStatus.Ok, (await itemService.MergeAsync(mergedId, survivorId)).Status);
        }

        // Re-run: Tier 1 regroups Foodstuffs by SKU, but the merged-away C3 key must resolve to the survivor.
        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var after = NewContext();
        var c3 = await after.Products.SingleAsync(p => p.Sku == "C3");
        Assert.Equal(survivorId, c3.ItemId);                                  // linked to the survivor, not recreated
        var tombstone = await after.Items.SingleAsync(c => c.MatchKey == "foodstuffs:C3");
        Assert.Equal(survivorId, tombstone.MergedIntoId);                     // stays a redirect tombstone
        Assert.Equal(0, await after.Products.CountAsync(p => p.ItemId == mergedId));
        Assert.Equal(2, await after.Items.CountAsync(c => c.MergedIntoId == null)); // Colby + the C2 survivor
    }

    // ---- FreshChoice: brand inferred from the name against the known-brand vocabulary (plan D29) --------------

    [Fact]
    public async Task Freshchoice_listing_gets_its_brand_inferred_from_the_name_and_autolinks()
    {
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(NewWorld, "M1", "Original Milk", "Meadow Fresh", "1L", 3.80m),
                // No RawBrand — the crawler never captures one for FreshChoice (D26). "Meadow Fresh" is the name's
                // leading two words and IS a brand Tier 1 already knows (from the New World listing above).
                FcSp("fc-m1", "Meadow Fresh Milk Original", "1L", 4.10m));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var item = await check.Items.SingleAsync(c => c.MatchKey == "foodstuffs:M1");
        var fc = await check.Products.SingleAsync(p => p.Sku == "fc-m1");
        Assert.Equal(item.Id, fc.ItemId);
    }

    [Fact]
    public async Task Freshchoice_ampersand_brand_is_tried_whole_not_truncated_at_the_ampersand()
    {
        // Regression: a naive "leading 2 words" guess turns "Beak & Sons Pork Belly" into "Beak &" (the "&" eats
        // one of the two word slots), which matches nothing. The "&" must be skipped past, not counted.
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(NewWorld, "P1", "Smoky Maple Pork Belly", "Beak & Sons", "600g", 12.00m),
                FcSp("fc-p1", "Beak & Sons Smoky Maple Pork Belly", "600g", 13.50m));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var item = await check.Items.SingleAsync(c => c.MatchKey == "foodstuffs:P1");
        var fc = await check.Products.SingleAsync(p => p.Sku == "fc-p1");
        Assert.Equal(item.Id, fc.ItemId);
    }

    [Fact]
    public async Task Freshchoice_produce_with_no_foodstuffs_brand_becomes_a_freshchoice_singleton_no_candidates()
    {
        // D30 Tier 4: no leading word is a Foodstuffs brand, so it can't be a Foodstuffs miss — it becomes its own
        // FreshChoice-anchored singleton item (browsable, ready to merge later), not a wrong candidate.
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(NewWorld, "M1", "Original Milk", "Meadow Fresh", "1L", 3.80m),
                FcSp("fc-veg", "Fennel Bulbs", "1kg", 2.50m));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var fc = await check.Products.Include(p => p.Item).SingleAsync(p => p.Sku == "fc-veg");
        Assert.NotNull(fc.ItemId);
        Assert.Equal("freshchoice:fc-veg", fc.Item!.MatchKey);
        Assert.Equal(0, await check.MatchCandidates.CountAsync(m => m.ProductId == fc.Id));
    }

    [Fact]
    public async Task Freshchoice_listing_that_looks_like_a_foodstuffs_brand_but_misses_stays_unanchored()
    {
        // D30 guard: "Meadow Fresh …" infers a Foodstuffs brand but the size doesn't line up with the NW item, so
        // it's a suspected Tier-2 miss — it must NOT mint a freshchoice: singleton (that would duplicate the item it
        // belongs to and split the compare). Stays unanchored for review / size-normalisation.
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(NewWorld, "M9", "Original Milk", "Meadow Fresh", "1L", 3.80m),
                FcSp("fc-odd", "Meadow Fresh Milk Original", "750mL", 4.10m)); // brand known, size differs → miss
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var fc = await check.Products.SingleAsync(p => p.Sku == "fc-odd");
        Assert.Null(fc.ItemId);
        Assert.False(await check.Items.AnyAsync(i => i.MatchKey == "freshchoice:fc-odd"));
    }

    // ---- Tier 3/4: the Woolworths → FreshChoice anchor cascade for products Foodstuffs lacks (plan D30) ---------

    [Fact]
    public async Task Woolworths_product_foodstuffs_lacks_becomes_its_own_anchor_item()
    {
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
                new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(NewWorld, "N1", "Colby Cheese", "Mainland", "500g", 10m),   // seeds the Foodstuffs brand vocab
                Sp(Woolworths, "ww-strk", "Streaky Bacon", "Hellers", "250g", 6m)); // Hellers ∉ Foodstuffs vocab here
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var ww = await check.Products.Include(p => p.Item).SingleAsync(p => p.Sku == "ww-strk");
        Assert.Equal("woolworths:ww-strk", ww.Item!.MatchKey);
        Assert.Equal("Streaky Bacon", ww.Item.Description);      // seeded from the listing (D25)
    }

    [Fact]
    public async Task Woolworths_product_with_a_foodstuffs_brand_that_misses_is_not_anchored()
    {
        // Guard: brand IS a Foodstuffs brand but the size doesn't line up → a Tier-2 miss, not a Tier-3 anchor.
        // Anchoring it would duplicate the Foodstuffs item it belongs to and split the compare.
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = NewWorld, Chain = Chain.NewWorld, Name = "NW", Suburb = "x", IsActive = true },
                new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(NewWorld, "N2", "Colby Cheese", "Mainland", "500g", 10m),
                Sp(Woolworths, "ww-miss", "Colby Cheese Block", "Mainland", "1kg", 15m)); // Mainland ∈ vocab, size differs
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var ww = await check.Products.SingleAsync(p => p.Sku == "ww-miss");
        Assert.Null(ww.ItemId);
        Assert.False(await check.Items.AnyAsync(i => i.MatchKey == "woolworths:ww-miss"));
    }

    [Fact]
    public async Task Freshchoice_attaches_to_a_woolworths_anchor_for_shared_private_label()
    {
        // The payoff: Woolworths-family private label sold at BOTH Woolworths and FreshChoice — impossible to
        // compare while items were Foodstuffs-anchored (neither carries a Foodstuffs brand). "WW" isn't in the
        // Foodstuffs vocab, so FC infers it against the Woolworths-anchor vocab and attaches (D30 Tier 3b).
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(Woolworths, "ww-colby", "WW Cheese Colby", "WW", "500g", 8m),
                FcSp("fc-colby", "WW Cheese Colby", "500g", 8.5m));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var ww = await check.Products.SingleAsync(p => p.Sku == "ww-colby");
        var fc = await check.Products.SingleAsync(p => p.Sku == "fc-colby");
        Assert.NotNull(ww.ItemId);
        Assert.Equal(ww.ItemId, fc.ItemId);                     // same Woolworths-anchored item → a real 2-store group
        var item = await check.Items.SingleAsync(i => i.Id == ww.ItemId);
        Assert.Equal("woolworths:ww-colby", item.MatchKey);
    }

    [Fact]
    public async Task Tier3_and_tier4_anchors_are_idempotent_on_rerun()
    {
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(Woolworths, "ww-bac", "Streaky Bacon", "Hellers", "250g", 6m),
                FcSp("fc-bac", "Hellers Streaky Bacon", "250g", 6.5m),   // attaches to the WW anchor (Tier 3b)
                FcSp("fc-kiwi", "Kiwifruit Gold Punnet", "1kg", 4m));    // FC-only → Tier 4 singleton
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();
        int itemsAfterFirst;
        await using (var check = NewContext()) itemsAfterFirst = await check.Items.CountAsync();

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var after = NewContext();
        Assert.Equal(itemsAfterFirst, await after.Items.CountAsync());   // no duplicates on re-run
        var wwItem = await after.Products.Where(p => p.Sku == "ww-bac").Select(p => p.ItemId).SingleAsync();
        var fcBacItem = await after.Products.Where(p => p.Sku == "fc-bac").Select(p => p.ItemId).SingleAsync();
        Assert.Equal(wwItem, fcBacItem);                                 // FC still grouped with WW
        var kiwi = await after.Products.Include(p => p.Item).SingleAsync(p => p.Sku == "fc-kiwi");
        Assert.Equal("freshchoice:fc-kiwi", kiwi.Item!.MatchKey);        // FC singleton stable
    }

    [Fact]
    public async Task Freshchoice_that_looks_like_a_woolworths_brand_but_misses_is_held_not_a_singleton()
    {
        // Generic guard (D30): "WW" is a Woolworths-anchor brand, not a Foodstuffs brand. This FC listing infers
        // "WW" but its size doesn't line up with the WW anchor, so it can't attach (Tier 3b) — and it must NOT then
        // mint a freshchoice: singleton (that would split a WW+FC compare once size normalisation improves). The
        // guard has to check the Woolworths-anchor vocab too, not just Foodstuffs.
        await using (var db = NewContext())
        {
            db.Stores.AddRange(
                new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true },
                new Store { Id = FreshChoice, Chain = Chain.FreshChoice, Name = "FC", Suburb = "x", IsActive = true });
            db.Products.AddRange(
                Sp(Woolworths, "ww-colby", "WW Cheese Colby", "WW", "500g", 8m),      // → woolworths: anchor, brand "WW"
                FcSp("fc-colby", "WW Cheese Colby Block", "1kg", 9m));                 // infers "WW", size differs → miss
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var fc = await check.Products.SingleAsync(p => p.Sku == "fc-colby");
        Assert.Null(fc.ItemId);                                                        // held, not anchored
        Assert.False(await check.Items.AnyAsync(i => i.MatchKey == "freshchoice:fc-colby"));
    }

    // ---- AutoLinked is run-scoped (plan D29 — was a DB-wide cumulative count) ------------------------------------

    [Fact]
    public async Task AutoLinked_counts_only_products_newly_linked_this_run()
    {
        await SeedAsync();

        MatchRunResult first, second;
        await using (var db = NewContext()) first = await Matcher(db).RunAsync();
        // Tier 1 links every Foodstuffs listing (C1@NW, C1@PAK, C2@NW, C3@NW = 4) + Tier 2's W1 auto-link = 5.
        // W2 stays pending, not linked.
        Assert.Equal(5, first.AutoLinked);

        await using (var db = NewContext()) second = await Matcher(db).RunAsync();
        // Re-run over unchanged data: everything that's linked was ALREADY linked before this run started.
        Assert.Equal(0, second.AutoLinked);
    }

    [Fact]
    public async Task Manually_created_item_survives_a_run_and_receives_a_woolworths_autolink()
    {
        // A hand-made item (create-item stamps a "manual:" MatchKey) with no products yet, plus a matching
        // Woolworths listing.
        await using (var db = NewContext())
        {
            db.Stores.Add(new Store { Id = Woolworths, Chain = Chain.Woolworths, Name = "WW", Suburb = "x", IsActive = true });
            db.Items.Add(new Item
            {
                MatchKey = "manual:abc", Name = "Acme Special Widget 500g", Description = "Acme Special Widget 500g",
                Brand = "Acme", Size = "500g", Category = "Widgets",
            });
            db.Products.Add(Sp(Woolworths, "WX", "acme special widget", "Acme", "500g", 5m));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Matcher(db).RunAsync();

        await using var check = NewContext();
        var manual = await check.Items.SingleAsync(c => c.MatchKey == "manual:abc");
        var wx = await check.Products.SingleAsync(p => p.Sku == "WX");
        Assert.Equal(manual.Id, wx.ItemId);              // auto-linked to the hand-made item via the (brand,size) index
        Assert.Equal(1, await check.Items.CountAsync()); // not duplicated/overwritten by the run
    }
}
