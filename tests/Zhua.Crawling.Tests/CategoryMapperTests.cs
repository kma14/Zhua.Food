using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Zhua.Application.Matching;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;
using Zhua.Infrastructure.Repositories;

namespace Zhua.Crawling.Tests;

/// <summary>Proves the category tree is built from Foodstuffs and products get categorised (plan D22).</summary>
public class CategoryMapperTests
{
    private readonly InMemoryDatabaseRoot _root = new();

    private DbContextOptions<ZhuaDbContext> Options() =>
        new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseInMemoryDatabase(nameof(CategoryMapperTests), _root)
            // Each test method gets its own isolated InMemoryDatabaseRoot by design — that's dozens of internal
            // service providers across the suite, which is exactly what this warning flags in production but is
            // the correct pattern for test isolation here.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

    private ZhuaDbContext NewContext() => new(Options());

    // The mapper is now an Application use case over the matching repository port.
    private static CategoryMapper Mapper(ZhuaDbContext db) => new(new MatchingRepository(db), new UnitOfWork(db));

    [Fact]
    public async Task Builds_tree_from_foodstuffs_maps_woolworths_by_name_and_categorises_products()
    {
        await SeedAsync();

        await using (var db = NewContext())
        {
            var result = await Mapper(db).MapAsync();
            Assert.Equal(2, result.Categories);   // Department + Shelf
            Assert.Equal(3, result.MappedStoreCategories);  // NW dept + NW shelf (identity) + WW shelf (by name)
            Assert.Equal(1, result.CategorizedProducts);
        }

        await using (var db = NewContext())
        {
            // Tree seeded from the Foodstuffs taxonomy, path built from the slugified name chain.
            var shelf = await db.Categories.SingleAsync(c => c.Kind == CategoryKind.Shelf);
            Assert.Equal("Beef Steaks", shelf.Name);
            Assert.Equal("meat-poultry-seafood/beef-steaks", shelf.Path);

            // The item is categorised from its Foodstuffs member's finest mapped category.
            var cp = await db.Items.SingleAsync();
            Assert.Equal(shelf.Id, cp.CategoryId);
            Assert.Equal("Beef Steaks", cp.Category);

            // Woolworths' identically-named shelf maps into the same item node.
            var wwShelf = await db.StoreCategories
                .SingleAsync(c => c.Store.Chain == Chain.Woolworths && c.Kind == CategoryKind.Shelf);
            Assert.Equal(shelf.Id, wwShelf.CategoryId);
        }
    }

    [Fact]
    public async Task Archived_node_stays_archived_on_rerun_and_products_bubble_to_the_live_ancestor()
    {
        // A NW product sitting under both its Department and its Shelf store categories (D11 many-to-many).
        await using (var db = NewContext())
        {
            var nw = new Store { Chain = Chain.NewWorld, Name = "NW", Suburb = "X", Latitude = -36.8, Longitude = 174.7 };
            db.Stores.Add(nw);
            var dept = new StoreCategory { Store = nw, Kind = CategoryKind.Department, ExternalId = "Meat, Poultry & Seafood", Slug = "meat", Name = "Meat, Poultry & Seafood" };
            var shelf = new StoreCategory { Store = nw, Kind = CategoryKind.Shelf, ExternalId = "Beef Steaks", Slug = "beef-steaks", Name = "Beef Steaks", Parent = dept };
            db.StoreCategories.AddRange(dept, shelf);
            var canon = new Item { Name = "Angus Sirloin 300g", Category = "Uncategorized" };
            var p = new Product { Store = nw, Sku = "NW-1", RawName = "Angus Sirloin 300g", FirstSeenAt = DateTimeOffset.UtcNow, Item = canon };
            p.Categories.Add(dept);
            p.Categories.Add(shelf);
            db.Products.Add(p);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Mapper(db).MapAsync();

        Guid shelfId;
        await using (var db = NewContext())
        {
            var shelf = await db.Categories.SingleAsync(c => c.Kind == CategoryKind.Shelf);
            shelfId = shelf.Id;
            Assert.Equal(shelf.Id, (await db.Items.SingleAsync()).CategoryId); // finest = shelf
        }

        // Archive the shelf, then re-run the mapper.
        await using (var db = NewContext())
        {
            var shelf = await db.Categories.SingleAsync(c => c.Id == shelfId);
            shelf.IsArchived = true;
            await db.SaveChangesAsync();
        }
        await using (var db = NewContext()) await Mapper(db).MapAsync();

        await using (var check = NewContext())
        {
            var shelf = await check.Categories.SingleAsync(c => c.Id == shelfId);
            Assert.True(shelf.IsArchived);                                                   // not un-archived
            Assert.Equal(1, await check.Categories.CountAsync(c => c.Kind == CategoryKind.Shelf)); // no duplicate at the path

            var dept = await check.Categories.SingleAsync(c => c.Kind == CategoryKind.Department);
            Assert.Equal(dept.Id, (await check.Items.SingleAsync()).CategoryId); // bubbled up
        }
    }

    [Fact]
    public async Task Department_alias_maps_a_woolworths_anchored_item_with_no_foodstuffs_member()
    {
        // Shared tree gets its "Meat, Poultry & Seafood" department from a Foodstuffs store category…
        await using (var db = NewContext())
        {
            var nw = new Store { Chain = Chain.NewWorld, Name = "NW", Suburb = "X", Latitude = -36.8, Longitude = 174.7 };
            var ww = new Store { Chain = Chain.Woolworths, Name = "WW", Suburb = "Y", Latitude = -36.8, Longitude = 174.7 };
            db.Stores.AddRange(nw, ww);

            var nwDept = new StoreCategory { Store = nw, Kind = CategoryKind.Department, ExternalId = "Meat, Poultry & Seafood", Slug = "meat", Name = "Meat, Poultry & Seafood" };
            db.StoreCategories.Add(nwDept);
            // A NW product just to seed the tree (its department node).
            var nwCanon = new Item { Name = "Seed", Category = "Uncategorized" };
            var nwP = new Product { Store = nw, Sku = "NW-1", RawName = "Seed", FirstSeenAt = DateTimeOffset.UtcNow, Item = nwCanon };
            nwP.Categories.Add(nwDept);
            db.Products.Add(nwP);

            // …and a Woolworths-anchored item (D30) whose only store category is WW's differently-named
            // "Meat & Poultry" department — no Foodstuffs member, so it can only be categorised via the alias.
            var wwDept = new StoreCategory { Store = ww, Kind = CategoryKind.Department, ExternalId = "1", Slug = "meat-poultry", Name = "Meat & Poultry" };
            db.StoreCategories.Add(wwDept);
            var wwCanon = new Item { MatchKey = "woolworths:WW-9", Name = "Macro Free Range Beef", Category = "Uncategorized" };
            var wwP = new Product { Store = ww, Sku = "WW-9", RawName = "Macro Free Range Beef", FirstSeenAt = DateTimeOffset.UtcNow, Item = wwCanon };
            wwP.Categories.Add(wwDept);
            db.Products.Add(wwP);

            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await Mapper(db).MapAsync();

        await using (var check = NewContext())
        {
            var dept = await check.Categories.SingleAsync(c => c.Kind == CategoryKind.Department);
            Assert.Equal("Meat, Poultry & Seafood", dept.Name);

            // The WW department was aliased onto the shared department…
            var wwDept = await check.StoreCategories.SingleAsync(c => c.Store.Chain == Chain.Woolworths);
            Assert.Equal(dept.Id, wwDept.CategoryId);

            // …so the Woolworths-anchored item is no longer Uncategorized.
            var wwItem = await check.Items.SingleAsync(i => i.MatchKey == "woolworths:WW-9");
            Assert.Equal(dept.Id, wwItem.CategoryId);
            Assert.Equal("Meat, Poultry & Seafood", wwItem.Category);
        }
    }

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        var nw = new Store { Chain = Chain.NewWorld, Name = "NW", Suburb = "X", Latitude = -36.8, Longitude = 174.7 };
        var ww = new Store { Chain = Chain.Woolworths, Name = "WW", Suburb = "Y", Latitude = -36.8, Longitude = 174.7 };
        db.Stores.AddRange(nw, ww);

        var nwDept = new StoreCategory { Store = nw, Kind = CategoryKind.Department, ExternalId = "Meat, Poultry & Seafood", Slug = "meat", Name = "Meat, Poultry & Seafood" };
        var nwShelf = new StoreCategory { Store = nw, Kind = CategoryKind.Shelf, ExternalId = "Beef Steaks", Slug = "beef-steaks", Name = "Beef Steaks", Parent = nwDept };
        var wwShelf = new StoreCategory { Store = ww, Kind = CategoryKind.Shelf, ExternalId = "1234", Slug = "beef-steaks", Name = "Beef Steaks" };
        db.StoreCategories.AddRange(nwDept, nwShelf, wwShelf);

        var canon = new Item { Name = "Angus Sirloin 300g", Category = "Uncategorized" };
        var nwProduct = new Product { Store = nw, Sku = "NW-1", RawName = "Angus Sirloin 300g", FirstSeenAt = DateTimeOffset.UtcNow, Item = canon };
        nwProduct.Categories.Add(nwShelf);
        var wwProduct = new Product { Store = ww, Sku = "WW-1", RawName = "Angus Beef Sirloin", FirstSeenAt = DateTimeOffset.UtcNow, Item = canon };
        wwProduct.Categories.Add(wwShelf);
        db.Products.AddRange(nwProduct, wwProduct);

        await db.SaveChangesAsync();
    }
}
