using System.Text.Json;
using Zhua.Application.Crawling;
using Zhua.Crawling.Woolworths;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.Tests;

/// <summary>Golden-file tests for the Woolworths JSON parser (plan D2/D13) — field mapping + promo tags.</summary>
public class WoolworthsParserTests
{
    private static List<ScrapedProduct> ParseFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "woolworths-products.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var into = new List<ScrapedProduct>();
        var path0 = new[] { new ScrapedCategoryNode(CategoryKind.Shelf, "1", "snapper", "Snapper") };
        WoolworthsCrawler.ParseProductsInto(doc.RootElement, path0, into);
        return into;
    }

    [Fact]
    public void Skips_non_product_items()
    {
        var products = ParseFixture();
        Assert.Equal(5, products.Count); // the PromotionalCarousel item is filtered out
        Assert.DoesNotContain(products, p => p.Name.Contains("must be skipped"));
    }

    [Fact]
    public void Maps_core_and_price_fields_for_a_special()
    {
        var p = ParseFixture().Single(x => x.Sku == "265262");

        Assert.Equal("Woolworths Fresh NZ Fish Fillets Snapper Tamure 1-3 Pce Tray", p.Name);
        Assert.Equal("Woolworths", p.Brand);
        Assert.Equal("9400000265262", p.Gtin);
        Assert.Equal("https://img/big.jpg", p.ImageUrl);
        Assert.Equal("1-3 Pieces", p.Size);
        Assert.Equal(46.9m, p.Price);            // salePrice = price paid now
        Assert.Equal(PromoType.Special, p.PromoType);
        Assert.Equal(52.4m, p.NonSpecialPrice);  // originalPrice = "was"
        Assert.Equal(46.9m, p.UnitPrice);
        Assert.Equal("1kg", p.UnitOfMeasure);
        Assert.Equal("Snapper", p.Category);     // last node of the path
    }

    [Fact]
    public void Club_price_maps_to_member_price_with_the_shelf_price_as_price()
    {
        // isClubPrice ⊂ isSpecial at the source — the club product is ALSO flagged isSpecial, and must not
        // come out as a public special (docs/internals/promotions-model.md).
        var p = ParseFixture().Single(x => x.Sku == "6006733");

        Assert.Equal(PromoType.MemberPrice, p.PromoType);
        Assert.Equal(12.6m, p.Price);            // originalPrice = what a cardless shopper pays
        Assert.Equal(11.95m, p.MemberPrice);     // salePrice = the club price
        Assert.Null(p.NonSpecialPrice);          // shelf price isn't discounted → no "was"
        Assert.Equal(84.0m, p.UnitPrice);        // cupListPrice matches the shelf price
        Assert.Contains(p.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "IsClubPrice");
    }

    [Fact]
    public void Multibuy_captures_the_quantity_total_pair()
    {
        var p = ParseFixture().Single(x => x.Sku == "777777");

        Assert.Equal(PromoType.Multibuy, p.PromoType);
        Assert.Equal(9.0m, p.Price);             // unit shelf price unaffected by "3 for $20"
        Assert.Equal(3, p.MultibuyQuantity);
        Assert.Equal(20.0m, p.MultibuyTotal);
        Assert.Null(p.MemberPrice);
    }

    [Fact]
    public void Captures_primary_promo_tag()
    {
        var special = ParseFixture().Single(x => x.Sku == "265262");
        Assert.Contains(special.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "IsSpecial");
    }

    [Fact]
    public void Captures_low_price_tag_even_without_a_discount()
    {
        var lowPrice = ParseFixture().Single(x => x.Sku == "123456");

        Assert.Equal(PromoType.None, lowPrice.PromoType); // no discount: salePrice == originalPrice
        Assert.Null(lowPrice.NonSpecialPrice);
        // …but the "Low Price" badge is still captured (the gap our schema previously missed).
        Assert.Contains(lowPrice.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "IsGreatPrice");
    }

    [Fact]
    public void Drops_the_meaningless_other_tag()
    {
        var regular = ParseFixture().Single(x => x.Sku == "999999");
        Assert.Empty(regular.Tags); // tagType "Other" = no real promo → no tag
    }

    // ---- Category identity (2026-07-26: milk filed under "Carrots & Root Vegetables") ---------------------------

    private static IReadOnlyList<ScrapedCategoryNode> BuildPath(string breadcrumbJson, params (CategoryKind Kind, string Slug)[] filters)
    {
        using var doc = JsonDocument.Parse($$"""{"breadcrumb":{{breadcrumbJson}}}""");
        return WoolworthsCrawler.BuildPath(doc.RootElement, filters.ToList());
    }

    private const string MilkBreadcrumb = """
        {"department":{"value":4,"name":"Fridge & Deli"},
         "aisle":{"value":27,"name":"Milk"},
         "shelf":{"value":666,"name":"Enriched Milk"}}
        """;

    [Fact]
    public void Category_identity_is_the_requested_slug_path_not_the_sources_numeric_id()
    {
        // Woolworths RECYCLES its numeric breadcrumb ids: shelf 666 means "Enriched Milk" today and meant
        // "Carrots & Root Vegetables" earlier, so keying on it re-filed milk under a vegetable node. The identity
        // has to be the dasFilter slugs we send; the source's name is display only.
        var path = BuildPath(MilkBreadcrumb,
            (CategoryKind.Department, "fridge-deli"), (CategoryKind.Aisle, "milk"), (CategoryKind.Shelf, "enriched-milk"));

        Assert.Equal(
            ["fridge-deli", "fridge-deli/milk", "fridge-deli/milk/enriched-milk"],
            path.Select(n => n.ExternalId));
        Assert.Equal(["fridge-deli", "milk", "enriched-milk"], path.Select(n => n.Slug));
        Assert.Equal(["Fridge & Deli", "Milk", "Enriched Milk"], path.Select(n => n.Name));
    }

    [Fact]
    public void Same_shelf_slug_under_two_aisles_stays_two_identities()
    {
        // A bare slug is not unique — "carrots-root-vegetables" hangs under both aisles, and Woolworths itself
        // keeps them as separate nodes (ids 674 and 243). Hence the full path, not just the leaf slug.
        const string underVegetables = """
            {"department":{"value":1,"name":"Fruit & Veg"},"aisle":{"value":117,"name":"Vegetables"},
             "shelf":{"value":674,"name":"Carrots & Root Vegetables"}}
            """;
        const string underSalad = """
            {"department":{"value":1,"name":"Fruit & Veg"},"aisle":{"value":119,"name":"Fresh Salad & Herbs"},
             "shelf":{"value":243,"name":"Carrots & Root Vegetables"}}
            """;

        var a = BuildPath(underVegetables,
            (CategoryKind.Department, "fruit-veg"), (CategoryKind.Aisle, "vegetables"), (CategoryKind.Shelf, "carrots-root-vegetables"));
        var b = BuildPath(underSalad,
            (CategoryKind.Department, "fruit-veg"), (CategoryKind.Aisle, "fresh-salad-herbs"), (CategoryKind.Shelf, "carrots-root-vegetables"));

        Assert.Equal("fruit-veg/vegetables/carrots-root-vegetables", a[^1].ExternalId);
        Assert.Equal("fruit-veg/fresh-salad-herbs/carrots-root-vegetables", b[^1].ExternalId);
    }
}
