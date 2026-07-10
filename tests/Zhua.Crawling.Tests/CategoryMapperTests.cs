using Microsoft.EntityFrameworkCore;
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
