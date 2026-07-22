using Zhua.Application.Crawling;
using Zhua.Crawling.FreshChoice;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.Tests;

/// <summary>
/// Golden-file tests for the FreshChoice/MyFoodLink HTML parser (plan D26) — the fixture is real card markup
/// captured 2026-07-19 from hc.store.freshchoice.co.nz (a special w/ was-price, a "3 for $20" multibuy deal,
/// a weight-sold regular product, a placeholder). Promo mapping per docs/internals/promotions-model.md.
/// </summary>
public class FreshChoiceParserTests
{
    private const string BaseUrl = "https://hc.store.freshchoice.co.nz";

    private static readonly ScrapedCategoryNode[] Path =
        [new(CategoryKind.Department, "meat", "meat", "Meat")];

    private static (List<ScrapedProduct> Products, string? Next) Parse()
    {
        var file = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "freshchoice-category.html");
        return FreshChoiceParser.ParsePage(File.ReadAllText(file), Path, BaseUrl);
    }

    [Fact]
    public void Parses_sellable_cards_and_skips_the_placeholder()
    {
        var (products, _) = Parse();
        Assert.Equal(3, products.Count); // placeholder card has no sku-price → skipped
    }

    [Fact]
    public void Special_maps_price_was_and_promo_type()
    {
        var p = Parse().Products.Single(x => x.Sku == "6a3e07caf83bb1e8db52653e");

        Assert.Equal("Chesdale Cheese Slices Chives", p.Name);
        Assert.Equal("250g", p.Size);
        Assert.Equal(4.00m, p.Price);
        Assert.Equal(5.40m, p.NonSpecialPrice);          // "was $5.40" — published directly, no D23 needed
        Assert.Equal(PromoType.Special, p.PromoType);
        Assert.Equal(1.60m, p.UnitPrice);                // "$1.60 per 100g"
        Assert.Equal("100g", p.UnitOfMeasure);
        Assert.Equal(BaseUrl + "/lines/chesdale-cheese-slices-chives-250g", p.Url);
        Assert.StartsWith("https://dtgxwmigmg3gc.cloudfront.net/", p.ImageUrl);
        Assert.Equal("Meat", p.Category);
        Assert.Contains(p.Tags, t => t.Source == ProductTagSource.Primary && t.Code == "Special" && t.Label == "save $1.40");
    }

    [Fact]
    public void Multibuy_deal_captures_the_pair_and_leaves_the_unit_price_untouched()
    {
        var p = Parse().Products.Single(x => x.Sku == "6a4c7bf8c779739eafd73016");

        Assert.Equal("Ocean Blue Salmon Double Smoked", p.Name);
        Assert.Equal(PromoType.Multibuy, p.PromoType);
        Assert.Equal(8.90m, p.Price);                    // unit shelf price unaffected by "3 for $20"
        Assert.Equal(3, p.MultibuyQuantity);
        Assert.Equal(20.00m, p.MultibuyTotal);
        Assert.Null(p.NonSpecialPrice);
        Assert.Null(p.MemberPrice);
        Assert.Equal(111.25m, p.UnitPrice);
        Assert.Equal("1kg", p.UnitOfMeasure);
        Assert.Contains(p.Tags, t => t.Code == "Deal");
    }

    [Fact]
    public void Weight_sold_regular_product_uses_the_sell_price_as_unit_price()
    {
        var p = Parse().Products.Single(x => x.Sku == "6a3e095af83bb1e8db52e820");

        Assert.Equal("Blue Cod Skinned & Boned", p.Name);
        Assert.Null(p.Size);                             // weight-sold: no size span
        Assert.Equal(PromoType.None, p.PromoType);
        Assert.Equal(48.99m, p.Price);                   // "$48.99 per kg"
        Assert.Equal(48.99m, p.UnitPrice);
        Assert.Equal("1kg", p.UnitOfMeasure);
        Assert.Empty(p.Tags);
    }

    [Fact]
    public void Follows_rel_next_pagination()
    {
        var (_, next) = Parse();
        Assert.Equal("/category/meat?page=2", next);
    }

    // ---- Pack size recovered from the name when the size span is empty (eggs, D26 follow-up) --------------------

    private static ScrapedProduct Card(string name, string? sizeSpan = null)
    {
        var size = sizeSpan is null ? "" : $"<span class='talker__name__size'>{sizeSpan}</span>";
        var html = $"<div class='talker' id='line_abc'>"
            + $"<div class='talker__product-name'>{name}</div>{size}"
            + "<strong class='price__sell'>$4.00</strong></div>";
        return FreshChoiceParser.ParsePage(html, Path, BaseUrl).Products.Single();
    }

    [Theory]
    [InlineData("Henergy Eggs Cage Free Barn Grade 6 12 Pack", "12pk")] // grade "6" not mistaken for the count
    [InlineData("Hens Choice Cage Free Eggs Size 7 12pk", "12pk")]      // "12pk" glued form
    [InlineData("Hens Choice Eggs Barn Mxd 6pack", "6pk")]              // "6pack" glued form
    [InlineData("Natural SPCA Free Range Eggs Tray 20 Pack", "20pk")]
    public void Pack_size_is_recovered_from_the_name_when_the_span_is_empty(string name, string expected) =>
        Assert.Equal(expected, Card(name).Size);

    [Fact]
    public void An_explicit_size_span_still_wins_over_the_name()
    {
        // A card that has both a real size span and a "Pack" in the name → the span is authoritative.
        Assert.Equal("250g", Card("Something 12 Pack", sizeSpan: "250g").Size);
    }

    [Fact]
    public void A_name_with_no_pack_quantity_leaves_size_null()
    {
        Assert.Null(Card("Blue Cod Skinned & Boned").Size);
    }
}
