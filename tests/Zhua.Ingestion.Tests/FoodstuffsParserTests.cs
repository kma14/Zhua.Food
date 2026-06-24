using System.Text.Json;
using Zhua.Application.Ingestion;
using Zhua.Crawling.Foodstuffs;
using Zhua.Domain.Enums;

namespace Zhua.Ingestion.Tests;

/// <summary>Golden-file tests for the Foodstuffs parser (plan D15) — cents→dollars, multi-tree categories, promo tags.</summary>
public class FoodstuffsParserTests
{
    // NewWorldCrawler supplies DepartmentNames = Meat/Poultry/Seafood + Fruit & Veg; parsing logic is in the base.
    private static List<ScrapedProduct> ParseFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "foodstuffs-products.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var into = new List<ScrapedProduct>();
        new NewWorldCrawler().ParseProductsInto(doc.RootElement, into);
        return into;
    }

    [Fact]
    public void Converts_cents_to_dollars_and_maps_unit_price()
    {
        var p = ParseFixture().First(x => x.SourceSku == "5125914-KGM-000");
        Assert.Equal(26.99m, p.Price);           // 2699 cents
        Assert.Equal(26.99m, p.UnitPrice);       // 2699 cents
        Assert.Equal("1kg", p.UnitOfMeasure);
        Assert.Equal("kg", p.Size);              // displayName
        Assert.Null(p.Gtin);                     // Foodstuffs search API exposes no barcode
        Assert.Null(p.NonSpecialPrice);
    }

    [Fact]
    public void Derives_fsimg_image_url_from_the_sku_numeric_prefix()
    {
        var p = ParseFixture().First(x => x.SourceSku == "5349090-EA-000");
        // fsimg is keyed by the prefix before the first dash (5349090), NOT the full SKU.
        Assert.Equal("https://a.fsimg.co.nz/product/retail/fan/image/400x400/5349090.png", p.ImageUrl);
    }

    [Fact]
    public void Emits_one_product_per_category_tree()
    {
        // NZ Beef Prime Mince has two trees (Beef + Mince) → two entries with the same SKU.
        var copies = ParseFixture().Where(x => x.SourceSku == "5125914-KGM-000").ToList();
        Assert.Equal(2, copies.Count);
        Assert.Contains(copies, c => c.Category == "Beef Mince & Stir Fry");
        Assert.Contains(copies, c => c.Category == "Mince");
        Assert.All(copies, c => Assert.Equal(CategoryKind.Department, c.CategoryPath[0].Kind));
    }

    [Fact]
    public void Captures_brand_for_packaged_items()
    {
        var p = ParseFixture().First(x => x.SourceSku == "5349090-EA-000");
        Assert.Equal("Hellers", p.Brand);
        Assert.Equal("340g", p.Size);
        Assert.Equal(9.49m, p.Price);
    }

    [Fact]
    public void Maps_promotion_to_special_and_a_tag()
    {
        var p = ParseFixture().First(x => x.SourceSku == "5106653-KGM-000");
        Assert.True(p.IsOnSpecial);
        Assert.Null(p.NonSpecialPrice); // Foodstuffs gives the promo price, not a "was" price
        Assert.Contains(p.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "3000");
    }

    [Fact]
    public void Skips_category_trees_outside_crawled_departments()
    {
        // The cross-department item has a Meat tree and a Frozen tree; only the Meat one is emitted.
        var copies = ParseFixture().Where(x => x.SourceSku == "5999999-EA-000").ToList();
        Assert.Single(copies);
        Assert.Equal("Meat, Poultry & Seafood", copies[0].CategoryPath[0].Name);
    }
}
