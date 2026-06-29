using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

/// <summary>
/// A small, fully-known dataset seeded once per test run, on top of the migration-seeded stores (StoreSeed).
/// Fixed GUIDs so tests can assert exact shapes. Shape:
///   Meat,Poultry &amp; Seafood ▸ Beef ▸ Beef Mince
///                            ▸ Chicken
///   • Beef Mince 1kg  — at Woolworths (on special 15→12), New World 13.50, PAK'nSAVE Albany 11.00 (cheapest)
///   • Beef Eye Fillet — only at PAK'nSAVE Botany 30.00 (sits on the Beef aisle directly)
///   • Chicken Breast  — only at New World 9.99 (on the Chicken aisle)
///   + a price-history series, one Woolworths special (deals), and three pending match candidates.
/// </summary>
internal static class TestData
{
    // Categories
    public static readonly Guid DeptMeat = new("aaaa0000-0000-0000-0000-000000000001");
    public static readonly Guid AisleBeef = new("aaaa0000-0000-0000-0000-000000000002");
    public static readonly Guid ShelfBeefMince = new("aaaa0000-0000-0000-0000-000000000003");
    public static readonly Guid AisleChicken = new("aaaa0000-0000-0000-0000-000000000004");

    // Dedicated Frozen branch for category-CRUD tests — isolated so create/rename/archive don't touch the Meat tree.
    public static readonly Guid DeptFrozen = new("aaaa0000-0000-0000-0000-000000000010");
    public static readonly Guid AisleIceCream = new("aaaa0000-0000-0000-0000-000000000011");
    public static readonly Guid RenameMeShelf = new("aaaa0000-0000-0000-0000-000000000012");
    public static readonly Guid ArchiveMeShelf = new("aaaa0000-0000-0000-0000-000000000013");

    // Items
    public static readonly Guid BeefMince = new("bbbb0000-0000-0000-0000-000000000001");
    public static readonly Guid EyeFillet = new("bbbb0000-0000-0000-0000-000000000002");
    public static readonly Guid ChickenBreast = new("bbbb0000-0000-0000-0000-000000000003");
    public static readonly Guid FrozenProduct = new("bbbb0000-0000-0000-0000-000000000010"); // under ArchiveMeShelf

    // A throwaway item the match candidates point at, so approving one doesn't alter the products other
    // tests inspect. Uncategorised + a name no search probes, so it's invisible to every other test.
    public static readonly Guid MatchTarget = new("bbbb0000-0000-0000-0000-0000000000ff");

    // Match candidates (one per review action so the tests don't interfere)
    public static readonly Guid CandidateForList = new("cccc0000-0000-0000-0000-000000000001");
    public static readonly Guid CandidateToApprove = new("cccc0000-0000-0000-0000-000000000002");
    public static readonly Guid CandidateToReject = new("cccc0000-0000-0000-0000-000000000003");
    public static readonly Guid CandidateOnLinkTarget = new("cccc0000-0000-0000-0000-000000000004");
    public static readonly Guid CandidateOnCreateTarget = new("cccc0000-0000-0000-0000-000000000005");

    // Dedicated unmatched listings for the manual link / create-item actions.
    public static readonly Guid LinkTargetSp = new("dddd0000-0000-0000-0000-000000000001");
    public static readonly Guid CreateTargetSp = new("dddd0000-0000-0000-0000-000000000002");

    // The cheapest mince listing (PAK'nSAVE Albany) — the representative of the BeefMince group, so the
    // store-first /products cards + product-keyed detail/history drill in via this PRODUCT id (Phase 2).
    public static readonly Guid MincePak = new("dddd0000-0000-0000-0000-0000000000a3");

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-24T06:00:00Z");

    public static async Task SeedAsync(ZhuaDbContext db)
    {
        db.Categories.AddRange(
            Cat(DeptMeat, CategoryKind.Department, "Meat, Poultry & Seafood", "meat-poultry-seafood", "meat-poultry-seafood", null),
            Cat(AisleBeef, CategoryKind.Aisle, "Beef", "beef", "meat-poultry-seafood/beef", DeptMeat),
            Cat(ShelfBeefMince, CategoryKind.Shelf, "Beef Mince", "beef-mince", "meat-poultry-seafood/beef/beef-mince", AisleBeef),
            Cat(AisleChicken, CategoryKind.Aisle, "Chicken", "chicken", "meat-poultry-seafood/chicken", DeptMeat),
            // Frozen branch (category-CRUD tests only)
            Cat(DeptFrozen, CategoryKind.Department, "Frozen", "frozen", "frozen", null),
            Cat(AisleIceCream, CategoryKind.Aisle, "Ice Cream & Desserts", "ice-cream-desserts", "frozen/ice-cream-desserts", DeptFrozen),
            Cat(RenameMeShelf, CategoryKind.Shelf, "Rename Me", "rename-me", "frozen/ice-cream-desserts/rename-me", AisleIceCream),
            Cat(ArchiveMeShelf, CategoryKind.Shelf, "Archive Me", "archive-me", "frozen/ice-cream-desserts/archive-me", AisleIceCream));

        db.Items.AddRange(
            Canon(BeefMince, "Beef Mince 1kg", "beef mince (grouped)", "Pams", "1kg", "Beef Mince", ShelfBeefMince),
            Canon(EyeFillet, "Beef Eye Fillet", null, "Pams", null, "Beef", AisleBeef),
            Canon(ChickenBreast, "Chicken Breast", null, "Tegel", "500g", "Chicken", AisleChicken),
            Canon(FrozenProduct, "Vanilla Ice Cream 2L", "vanilla ice cream", "Tip Top", "2L", "Archive Me", ArchiveMeShelf),
            new Item { Id = MatchTarget, Name = "Generic Match Target", Category = "Uncategorised" });

        // Beef Mince across three stores — PAK'nSAVE Albany cheapest; Woolworths on special.
        db.Products.AddRange(
            Sp(StoreSeed.WoolworthsTakapuna, "ww-mince", "Woolworths Beef Mince", 12.00m, BeefMince,
                onSpecial: true, wasPrice: 15.00m, img: "https://assets.woolworths.com.au/images/ww-mince.jpg"),
            Sp(StoreSeed.NewWorldMetro, "nw-mince", "Pams Beef Mince 1kg", 13.50m, BeefMince,
                img: "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5125914.png"),
            Sp(StoreSeed.PaknSaveAlbany, "pns-mince", "Pams Beef Mince 1kg", 11.00m, BeefMince,
                img: "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5125914.png", id: MincePak),
            // Eye fillet only at PAK'nSAVE Botany (for the store-filter test)
            Sp(StoreSeed.PaknSaveBotany, "pns-fillet", "Pams Beef Eye Fillet", 30.00m, EyeFillet,
                img: "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5106653.png"),
            // Chicken breast only at New World
            Sp(StoreSeed.NewWorldMetro, "nw-chicken", "Tegel Chicken Breast 500g", 9.99m, ChickenBreast,
                img: "https://a.fsimg.co.nz/product/retail/fan/image/400x400/5105651.png"),
            // Frozen product under ArchiveMeShelf (so archiving the shelf hides it from browse)
            Sp(StoreSeed.WoolworthsTakapuna, "frozen-1", "Tip Top Vanilla Ice Cream 2L", 7.50m, FrozenProduct,
                img: "https://assets.woolworths.com.au/images/frozen.jpg"));

        await db.SaveChangesAsync();

        // Price history for the PAK'nSAVE Albany mince: 11.50 → 11.00.
        var pnsMince = db.Products.Single(sp => sp.SourceSku == "pns-mince");
        var run = new CrawlRun
        {
            StoreId = StoreSeed.PaknSaveAlbany, StartedAt = Now.AddDays(-1), FinishedAt = Now.AddDays(-1),
            Status = CrawlRunStatus.Succeeded, ProductsFound = 1, SnapshotsWritten = 2,
        };
        db.CrawlRuns.Add(run);
        await db.SaveChangesAsync();

        db.PriceSnapshots.AddRange(
            Snap(pnsMince.Id, run.Id, 11.50m, Now.AddDays(-1)),
            Snap(pnsMince.Id, run.Id, 11.00m, Now));
        await db.SaveChangesAsync();

        // Three unmatched store products, each with one pending match candidate → BeefMince item.
        db.Products.AddRange(
            Sp(StoreSeed.WoolworthsTakapuna, "unmatched-1", "Beef Mince Premium 1kg", 14.00m, itemId: null),
            Sp(StoreSeed.NewWorldShoreCity, "unmatched-2", "Beef Mince Value 1kg", 10.50m, itemId: null),
            Sp(StoreSeed.PaknSaveHighlandPark, "unmatched-3", "Beef Mince Basic 1kg", 9.50m, itemId: null));
        await db.SaveChangesAsync();

        // Two more unmatched listings (fixed ids) for the manual link / create-item actions.
        db.Products.AddRange(
            new Product
            {
                Id = LinkTargetSp, StoreId = StoreSeed.WoolworthsTakapuna, SourceSku = "link-target",
                RawName = "Link Me 1kg", CurrentPrice = 5.00m, UnitPrice = 5.00m, UnitOfMeasure = "1kg",
                FirstSeenAt = Now, LastSeenAt = Now,
            },
            new Product
            {
                Id = CreateTargetSp, StoreId = StoreSeed.NewWorldMetro, SourceSku = "create-target",
                RawName = "Create Me 500g", RawBrand = "Acme", RawSize = "500g",
                CurrentPrice = 6.00m, UnitPrice = 12.00m, UnitOfMeasure = "1kg", FirstSeenAt = Now, LastSeenAt = Now,
            });
        await db.SaveChangesAsync();

        Guid SpId(string sku) => db.Products.Single(sp => sp.SourceSku == sku).Id;
        db.MatchCandidates.AddRange(
            Candidate(CandidateForList, SpId("unmatched-1"), 0.60),
            Candidate(CandidateToApprove, SpId("unmatched-2"), 0.55),
            Candidate(CandidateToReject, SpId("unmatched-3"), 0.50),
            Candidate(CandidateOnLinkTarget, LinkTargetSp, 0.40),
            Candidate(CandidateOnCreateTarget, CreateTargetSp, 0.40));
        await db.SaveChangesAsync();
    }

    private static Category Cat(Guid id, CategoryKind kind, string name, string slug, string path, Guid? parent) =>
        new() { Id = id, Kind = kind, Name = name, Slug = slug, Path = path, ParentId = parent };

    private static Item Canon(Guid id, string name, string? description, string? brand, string? size, string category, Guid catId) =>
        new() { Id = id, Name = name, Description = description, Brand = brand, Size = size, Category = category, CategoryId = catId };

    private static Product Sp(
        Guid storeId, string sku, string name, decimal price, Guid? itemId,
        bool onSpecial = false, decimal? wasPrice = null, string? img = null, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            StoreId = storeId, SourceSku = sku, RawName = name, RawBrand = null, RawSize = null,
            ItemId = itemId, ImageUrl = img,
            CurrentPrice = price, IsOnSpecial = onSpecial, CurrentNonSpecialPrice = wasPrice,
            UnitPrice = price, UnitOfMeasure = "1kg",
            FirstSeenAt = Now, LastSeenAt = Now, PriceUpdatedAt = Now,
        };

    private static PriceSnapshot Snap(Guid spId, Guid runId, decimal price, DateTimeOffset at) =>
        new() { ProductId = spId, CrawlRunId = runId, Price = price, UnitPrice = price, CapturedAt = at };

    private static MatchCandidate Candidate(Guid id, Guid storeProductId, double score) =>
        new()
        {
            Id = id, ProductId = storeProductId, ItemId = MatchTarget, Score = score,
            Status = MatchStatus.Pending, CreatedAt = Now, Reason = "brand+size match, name overlap 0.50; ambiguous",
        };
}
