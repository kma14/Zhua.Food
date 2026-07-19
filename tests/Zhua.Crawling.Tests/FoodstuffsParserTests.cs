using System.Text.Json;
using Zhua.Application.Crawling;
using Zhua.Crawling.Foodstuffs;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.Tests;

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
        var p = ParseFixture().First(x => x.Sku == "5125914-KGM-000");
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
        var p = ParseFixture().First(x => x.Sku == "5349090-EA-000");
        // fsimg is keyed by the prefix before the first dash (5349090), NOT the full SKU.
        Assert.Equal("https://a.fsimg.co.nz/product/retail/fan/image/400x400/5349090.png", p.ImageUrl);
    }

    [Fact]
    public void Emits_one_product_per_category_tree()
    {
        // NZ Beef Prime Mince has two trees (Beef + Mince) → two entries with the same SKU.
        var copies = ParseFixture().Where(x => x.Sku == "5125914-KGM-000").ToList();
        Assert.Equal(2, copies.Count);
        Assert.Contains(copies, c => c.Category == "Beef Mince & Stir Fry");
        Assert.Contains(copies, c => c.Category == "Mince");
        Assert.All(copies, c => Assert.Equal(CategoryKind.Department, c.CategoryPath[0].Kind));
    }

    [Fact]
    public void Captures_brand_for_packaged_items()
    {
        var p = ParseFixture().First(x => x.Sku == "5349090-EA-000");
        Assert.Equal("Hellers", p.Brand);
        Assert.Equal("340g", p.Size);
        Assert.Equal(9.49m, p.Price);
    }

    [Fact]
    public void Maps_public_promotion_to_special_and_a_tag()
    {
        var p = ParseFixture().First(x => x.Sku == "5106653-KGM-000");
        Assert.Equal(PromoType.Special, p.PromoType);
        Assert.Null(p.NonSpecialPrice); // the shelf price IS the promo price; the regular price isn't published (D23)
        Assert.Null(p.MemberPrice);
        Assert.Contains(p.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "3000");
    }

    [Fact]
    public void Clubcard_promotion_maps_to_member_price_using_the_best_promotion_element()
    {
        // singlePrice = the NON-member shelf price; rewardValue = the club price. The first promotions[] element
        // is a decoy (bestPromotion: false) — the mapper must pick the flagged one (decision D1).
        var p = ParseFixture().First(x => x.Sku == "5039995-EA-000");

        Assert.Equal(PromoType.MemberPrice, p.PromoType);
        Assert.Equal(12.59m, p.Price);
        Assert.Equal(11.29m, p.MemberPrice);
        Assert.Null(p.NonSpecialPrice);          // shelf price isn't discounted → nothing for D23 to reconstruct
        Assert.Contains(p.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "4000");
    }

    [Fact]
    public void Clubcard_multibuy_keeps_member_type_and_captures_the_pair()
    {
        // threshold 3 / rewardValue 500 = "3 for $5.00" with the card; rewardValue is a TOTAL, so it must land
        // in the multibuy pair, never in MemberPrice or Price.
        var p = ParseFixture().First(x => x.Sku == "5222222-EA-000");

        Assert.Equal(PromoType.MemberPrice, p.PromoType);
        Assert.Equal(2.49m, p.Price);            // regular unit shelf price
        Assert.Null(p.MemberPrice);
        Assert.Equal(3, p.MultibuyQuantity);
        Assert.Equal(5.00m, p.MultibuyTotal);
    }

    [Fact]
    public void Skips_category_trees_outside_crawled_departments()
    {
        // The cross-department item has a Meat tree and a Frozen tree; only the Meat one is emitted.
        var copies = ParseFixture().Where(x => x.Sku == "5999999-EA-000").ToList();
        Assert.Single(copies);
        Assert.Equal("Meat, Poultry & Seafood", copies[0].CategoryPath[0].Name);
    }
}
