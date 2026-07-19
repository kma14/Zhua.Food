using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Zhua.Application.Crawling;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.FreshChoice;

/// <summary>
/// Parses a MyFoodLink category page (server-rendered HTML — no JSON API, D26) into products. The class names
/// (<c>talker*</c>, <c>price__*</c>) are the contract with the site; the selector table lives in
/// docs/internals/crawling.md "FreshChoice — MyFoodLink (D26)". Pure + internal for golden-file tests.
/// Promo mapping (docs/internals/promotions-model.md): a was-price or the <c>talker--Special</c> card class →
/// <see cref="PromoType.Special"/>; a "N for $X" deal ("Deal" sticker linking to /deals/…) → Multibuy with the
/// quantity+total pair; no member price exists on this platform (FreshChoice runs no loyalty program).
/// </summary>
internal static partial class FreshChoiceParser
{
    private static readonly HtmlParser Parser = new();

    /// <summary>Parses one category page: its product cards + the next page's path (null on the last page).</summary>
    internal static (List<ScrapedProduct> Products, string? NextPagePath) ParsePage(
        string html, IReadOnlyList<ScrapedCategoryNode> path, string baseUrl)
    {
        var doc = Parser.ParseDocument(html);
        var products = new List<ScrapedProduct>();

        foreach (var card in doc.QuerySelectorAll("div.talker[id^='line_']"))
        {
            if (card.ClassList.Contains("talker--placeholder")) continue;

            var sku = card.Id!["line_".Length..];
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var name = Text(card, ".talker__product-name");
            var priceText = Text(card, "strong.price__sell");
            if (name is null || Money(priceText) is not { } price) continue; // no name/price → not a sellable card

            // "each" for unit-sold items; "per kg" for weight-sold ones — for the latter the sell price IS the
            // unit price (like Foodstuffs KGM products) and no separate comparison span is rendered.
            var sellUnits = Text(card, "span.price__units");
            decimal? unitPrice = null;
            string? unitOfMeasure = null;
            if (Text(card, ".talker__prices__comparison--UnitPrice") is { } comparison
                && Money(comparison) is { } cmpPrice)
            {
                unitPrice = cmpPrice;                    // "$1.60 per 100g"
                unitOfMeasure = Measure(comparison);
            }
            else if (sellUnits is not null && sellUnits.StartsWith("per ", StringComparison.OrdinalIgnoreCase))
            {
                unitPrice = price;                       // weight-sold: "$48.99 per kg"
                unitOfMeasure = Measure(sellUnits);
            }

            // Multibuy pair from the additional-unit-price span ("3 for $20.00 - $83.33 per kg"), falling back
            // to the sticker label ("3 for<br>$20").
            int? multiQty = null;
            decimal? multiTotal = null;
            var dealText = Text(card, ".talker__additional_unit_prices__unit_price--multibuy")
                ?? (card.ClassList.Contains("talker--Deal") ? Text(card, ".talker__sticker__label") : null);
            if (dealText is not null && MultibuyPattern().Match(dealText) is { Success: true } mb)
            {
                multiQty = int.Parse(mb.Groups[1].Value, CultureInfo.InvariantCulture);
                multiTotal = decimal.Parse(mb.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            var wasPrice = Money(Text(card, ".talker__prices__was"));   // "was $5.40"
            if (wasPrice is not null && wasPrice <= price) wasPrice = null; // guard: never a non-discount "was"

            var promoType = wasPrice is not null || card.ClassList.Contains("talker--Special")
                ? PromoType.Special
                : multiQty is not null ? PromoType.Multibuy
                : PromoType.None;

            // D13 tags: the card's promo modifier classes, labelled with the sticker text ("save $1.40" / "3 for $20").
            var stickerLabel = Squash(Text(card, ".talker__sticker__label"));
            var tags = new List<ScrapedTag>();
            foreach (var code in (string[])["Special", "Discount", "Deal"])
                if (card.ClassList.Contains($"talker--{code}"))
                    tags.Add(new ScrapedTag(ProductTagSource.Primary, code, stickerLabel));

            var href = card.QuerySelector("a[href^='/lines/']")?.GetAttribute("href");

            products.Add(new ScrapedProduct
            {
                Sku = sku,
                Name = name,
                Brand = null,                            // not published separately (leading word(s) of the name)
                Size = Text(card, ".talker__name__size"),
                Gtin = null,                             // not published
                Url = href is null ? null : baseUrl + href,
                ImageUrl = card.QuerySelector(".talker__section--image img")?.GetAttribute("src"),
                Category = path.Count > 0 ? path[^1].Name : null,
                CategoryPath = path,
                Tags = tags,
                Price = price,
                NonSpecialPrice = wasPrice,              // published directly — no D23 reconstruction needed
                PromoType = promoType,
                MemberPrice = null,                      // no loyalty program on this platform
                MultibuyQuantity = multiQty,
                MultibuyTotal = multiTotal,
                UnitPrice = unitPrice,
                UnitOfMeasure = unitOfMeasure,
            });
        }

        var next = doc.QuerySelector("a[rel='next']")?.GetAttribute("href");
        return (products, string.IsNullOrWhiteSpace(next) ? null : next);
    }

    private static string? Text(IElement scope, string selector)
    {
        var text = scope.QuerySelector(selector)?.TextContent;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>First dollar amount in a text ("was $5.40" → 5.40; "save 20c" → null).</summary>
    private static decimal? Money(string? text) =>
        text is not null && MoneyPattern().Match(text) is { Success: true } m
            ? decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;

    /// <summary>"per kg" / "$1.60 per 100g" → the cupMeasure-style unit our normaliser understands.</summary>
    private static string? Measure(string text)
    {
        var m = PerUnitPattern().Match(text);
        if (!m.Success) return null;
        var unit = m.Groups[1].Value.ToLowerInvariant();
        return unit switch
        {
            "kg" => "1kg",
            "l" => "1L",
            "each" or "ea" => "1ea",
            _ => unit,                                   // already quantified, e.g. "100g", "100ml"
        };
    }

    private static string? Squash(string? text) =>
        text is null ? null : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\$(\d+(?:\.\d+)?)")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"(\d+)\s+for\s+\$(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex MultibuyPattern();

    [GeneratedRegex(@"per\s+([a-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PerUnitPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
