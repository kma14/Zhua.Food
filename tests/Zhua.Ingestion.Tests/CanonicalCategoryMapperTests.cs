using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Matching;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Ingestion.Tests;

/// <summary>Proves the canonical category tree is built from Foodstuffs and products get categorised (plan D22).</summary>
public class CanonicalCategoryMapperTests
{
    private readonly InMemoryDatabaseRoot _root = new();

    private DbContextOptions<ZhuaDbContext> Options() =>
        new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseInMemoryDatabase(nameof(CanonicalCategoryMapperTests), _root)
            .Options;

    private ZhuaDbContext NewContext() => new(Options());

    [Fact]
    public async Task Builds_tree_from_foodstuffs_maps_woolworths_by_name_and_categorises_products()
    {
        await SeedAsync();

        await using (var db = NewContext())
        {
            var result = await new CanonicalCategoryMapper(db).MapAsync();
            Assert.Equal(2, result.CanonicalCategories);   // Department + Shelf
            Assert.Equal(3, result.MappedStoreCategories);  // NW dept + NW shelf (identity) + WW shelf (by name)
            Assert.Equal(1, result.CategorizedProducts);
        }

        await using (var db = NewContext())
        {
            // Tree seeded from the Foodstuffs taxonomy, path built from the slugified name chain.
            var shelf = await db.CanonicalCategories.SingleAsync(c => c.Kind == CategoryKind.Shelf);
            Assert.Equal("Beef Steaks", shelf.Name);
            Assert.Equal("meat-poultry-seafood/beef-steaks", shelf.Path);

            // The canonical product is categorised from its Foodstuffs member's finest mapped category.
            var cp = await db.CanonicalProducts.SingleAsync();
            Assert.Equal(shelf.Id, cp.CanonicalCategoryId);
            Assert.Equal("Beef Steaks", cp.Category);

            // Woolworths' identically-named shelf maps into the same canonical node.
            var wwShelf = await db.StoreCategories
                .SingleAsync(c => c.Store.Chain == Chain.Woolworths && c.Kind == CategoryKind.Shelf);
            Assert.Equal(shelf.Id, wwShelf.CanonicalCategoryId);
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

        var canon = new CanonicalProduct { Name = "Angus Sirloin 300g", Category = "Uncategorized" };
        var nwProduct = new StoreProduct { Store = nw, SourceSku = "NW-1", RawName = "Angus Sirloin 300g", FirstSeenAt = DateTimeOffset.UtcNow, CanonicalProduct = canon };
        nwProduct.Categories.Add(nwShelf);
        var wwProduct = new StoreProduct { Store = ww, SourceSku = "WW-1", RawName = "Angus Beef Sirloin", FirstSeenAt = DateTimeOffset.UtcNow, CanonicalProduct = canon };
        wwProduct.Categories.Add(wwShelf);
        db.StoreProducts.AddRange(nwProduct, wwProduct);

        await db.SaveChangesAsync();
    }
}
