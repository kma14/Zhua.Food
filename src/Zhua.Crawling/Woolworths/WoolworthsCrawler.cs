using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Zhua.Application.Crawling;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.Woolworths;

/// <summary>
/// Woolworths NZ crawler (plan D2/D10): drives a real Chromium (headless is WAF-blocked, so headed), sets the
/// physical store by geolocation, then walks the store's category tree via the browse API and parses the JSON.
/// Aisles/shelves are auto-discovered from each response's <c>dasFacets</c>; every product is tagged with its
/// full category path (Department → Aisle → Shelf, D11). A product appears under several leaves (many-to-many),
/// so leaves are returned with duplicates — the orchestrator dedups the product and accumulates categories.
/// </summary>
public sealed class WoolworthsCrawler : IStoreCrawler
{
    public Chain Chain => Chain.Woolworths;

    // M1 department slugs (plan D10). Aisles/shelves under each are auto-discovered from the API facets.
    private static readonly string[] DepartmentSlugs =
        ["meat-poultry", "fruit-veg", "fish-seafood", "fridge-deli", "frozen"];

    private const int PageSize = 48;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/131.0.0.0 Safari/537.36 zhua.food-price-tracker";

    public async Task<ScrapeResult> FetchAsync(Store store, CancellationToken ct = default)
    {
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Headless is WAF-blocked → headed by default; ZHUA_CRAWL_HEADLESS overrides (e.g. once stealth/xvfb is added).
            Headless = Environment.GetEnvironmentVariable("ZHUA_CRAWL_HEADLESS") is not null,
            Args = ["--disable-http2", "--disable-blink-features=AutomationControlled"],
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Geolocation = new Geolocation { Latitude = (float)store.Latitude, Longitude = (float)store.Longitude },
            Permissions = ["geolocation"],
            Locale = "en-NZ",
            UserAgent = UserAgent,
        });
        var page = await context.NewPageAsync();

        // Raw-response archive for retrospective debugging (plan D12). Default on, 7-day self-pruning retention.
        var archive = RawCrawlArchive.FromEnvironment(Chain.ToString(), DateTimeOffset.UtcNow);

        // Warm up the homepage to establish session/store cookies.
        try
        {
            await page.GotoAsync("https://www.woolworths.co.nz",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            await page.WaitForTimeoutAsync(2_000);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { }

        var results = new List<ScrapedProduct>();
        var gaps = new List<string>();

        foreach (var deptSlug in DepartmentSlugs)
        {
            ct.ThrowIfCancellationRequested();

            var deptFilters = new List<(CategoryKind Kind, string Slug)> { (CategoryKind.Department, deptSlug) };
            var deptJson = await FetchBrowseAsync(page, archive, deptFilters, 1, ct);
            if (deptJson is null)
            {
                gaps.Add($"{deptSlug}: department listing failed after retries — whole department skipped");
                continue;
            }

            string[] aisleNames;
            using (var doc = JsonDocument.Parse(deptJson))
                aisleNames = FacetNames(doc.RootElement, "Aisle");

            foreach (var aisleName in aisleNames)
            {
                ct.ThrowIfCancellationRequested();
                var aisleFilters = new List<(CategoryKind Kind, string Slug)>(deptFilters) { (CategoryKind.Aisle, Slugify(aisleName)) };
                var aisleJson = await FetchBrowseAsync(page, archive, aisleFilters, 1, ct);
                if (aisleJson is null)
                {
                    gaps.Add($"{deptSlug}/{Slugify(aisleName)}: aisle listing failed after retries — whole aisle skipped");
                    continue;
                }

                string[] shelfNames;
                using (var doc = JsonDocument.Parse(aisleJson))
                    shelfNames = FacetNames(doc.RootElement, "Shelf");

                if (shelfNames.Length == 0)
                {
                    await CrawlLeafAsync(page, archive, aisleFilters, aisleJson, results, gaps, ct); // aisle is the leaf
                }
                else
                {
                    foreach (var shelfName in shelfNames)
                    {
                        ct.ThrowIfCancellationRequested();
                        var shelfFilters = new List<(CategoryKind Kind, string Slug)>(aisleFilters) { (CategoryKind.Shelf, Slugify(shelfName)) };
                        await CrawlLeafAsync(page, archive, shelfFilters, null, results, gaps, ct);
                    }
                }
            }
        }

        return new ScrapeResult(results, gaps);
    }

    /// <summary>Crawls one leaf category across all pages, appending products tagged with their full category path.
    /// Pages that stay missing after <see cref="FetchBrowseAsync"/>'s retries are recorded as gaps (plan D28).</summary>
    private static async Task CrawlLeafAsync(
        IPage page, RawCrawlArchive archive, List<(CategoryKind Kind, string Slug)> filters, string? firstPageJson,
        List<ScrapedProduct> results, List<string> gaps, CancellationToken ct)
    {
        var leafLabel = string.Join("/", filters.Select(f => f.Slug));
        var json = firstPageJson ?? await FetchBrowseAsync(page, archive, filters, 1, ct);
        if (json is null)
        {
            gaps.Add($"{leafLabel}: page 1 failed after retries");
            return;
        }

        int total;
        IReadOnlyList<ScrapedCategoryNode> path;
        using (var doc = JsonDocument.Parse(json))
        {
            total = TotalItems(doc.RootElement);
            path = BuildPath(doc.RootElement, filters);
            ParseProductsInto(doc.RootElement, path, results);
        }

        var pages = (int)Math.Ceiling(total / (double)PageSize);
        for (var p = 2; p <= pages; p++)
        {
            ct.ThrowIfCancellationRequested();
            var more = await FetchBrowseAsync(page, archive, filters, p, ct);
            if (more is null)
            {
                gaps.Add($"{leafLabel}: page {p} of {pages} failed after retries");
                break;
            }
            using var doc = JsonDocument.Parse(more);
            ParseProductsInto(doc.RootElement, path, results);
        }
    }

    /// <summary>
    /// Calls the browse products API from within the page (cookies + x-requested-with). Returns body or null.
    /// Woolworths' WAF rate-limits bursts (empty body = blocked), so on a block we cool down and re-establish the
    /// session before retrying, rather than skipping the category outright.
    /// </summary>
    private static async Task<string?> FetchBrowseAsync(
        IPage page, RawCrawlArchive archive, List<(CategoryKind Kind, string Slug)> filters, int pageNo, CancellationToken ct)
    {
        var sb = new StringBuilder("https://www.woolworths.co.nz/api/v1/products?");
        foreach (var (kind, slug) in filters)
            sb.Append("dasFilter=").Append(kind).Append("%3B%3B").Append(Uri.EscapeDataString(slug)).Append("%3Bfalse&");
        sb.Append("target=browse&inStockProductsOnly=false&size=").Append(PageSize).Append("&page=").Append(pageNo);
        var url = sb.ToString();

        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var res = await page.EvaluateAsync<string>(
                    "async (u) => { try { const r = await fetch(u, { headers: { 'x-requested-with': 'OnlineShopping.WebApp' }, credentials: 'include' }); return r.ok ? await r.text() : ''; } catch { return ''; } }",
                    url);
                await page.WaitForTimeoutAsync(600); // base politeness (plan D6)
                if (!string.IsNullOrEmpty(res))
                {
                    // Archive the raw response for retrospective debugging (plan D12).
                    var name = string.Join("__", filters.Select(f => f.Slug)) + $"_p{pageNo}";
                    await archive.SaveAsync(name, res, ct);
                    return res;
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { }

            // Empty/blocked → cool down (let the rate-limit window reset) and refresh the session, then retry.
            if (attempt < maxAttempts)
            {
                await page.WaitForTimeoutAsync(12_000 * attempt); // 12s, 24s, 36s
                try
                {
                    await page.GotoAsync("https://www.woolworths.co.nz",
                        new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { }
            }
        }
        return null;
    }

    private static IReadOnlyList<ScrapedCategoryNode> BuildPath(JsonElement root, List<(CategoryKind Kind, string Slug)> filters)
    {
        var bc = Obj(root, "breadcrumb");
        var nodes = new List<ScrapedCategoryNode>(filters.Count);
        foreach (var (kind, slug) in filters)
        {
            var key = kind switch { CategoryKind.Department => "department", CategoryKind.Aisle => "aisle", _ => "shelf" };
            var el = Obj(bc, key);
            var externalId = el.ValueKind == JsonValueKind.Object && el.TryGetProperty("value", out var v) ? v.ToString() : slug;
            nodes.Add(new ScrapedCategoryNode(kind, externalId, slug, Str(el, "name") ?? slug));
        }
        return nodes;
    }

    private static int TotalItems(JsonElement root)
    {
        var products = Obj(root, "products");
        return products.ValueKind == JsonValueKind.Object && products.TryGetProperty("totalItems", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32()
            : 0;
    }

    private static string[] FacetNames(JsonElement root, string group)
    {
        if (!root.TryGetProperty("dasFacets", out var facets) || facets.ValueKind != JsonValueKind.Array)
            return [];
        return facets.EnumerateArray()
            .Where(f => f.ValueKind == JsonValueKind.Object && string.Equals(Str(f, "group"), group, StringComparison.OrdinalIgnoreCase))
            .Select(f => Str(f, "name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToArray();
    }

    /// <summary>Parses Woolworths <c>/api/v1/products</c> JSON into products tagged with <paramref name="path"/>. Testable.</summary>
    internal static void ParseProductsInto(JsonElement root, IReadOnlyList<ScrapedCategoryNode> path, List<ScrapedProduct> into)
    {
        var products = Obj(root, "products");
        if (products.ValueKind != JsonValueKind.Object || !products.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!string.Equals(Str(item, "type"), "Product", StringComparison.OrdinalIgnoreCase)) continue;

            var sku = Str(item, "sku");
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var price = Obj(item, "price");
            var size = Obj(item, "size");
            var original = Dec(price, "originalPrice");
            var sale = Dec(price, "salePrice") ?? original;
            if (sale is null) continue;
            var isSpecial = Bool(price, "isSpecial");
            // isClubPrice ⊂ isSpecial at the source: a club deal is ALSO flagged isSpecial, so club must be
            // split out FIRST or member prices masquerade as public specials (docs/internals/promotions-model.md).
            var isClub = Bool(price, "isClubPrice");

            var productTag = Obj(item, "productTag");

            // Promo tag (plan D13): the primary badge. "Other" means no real promo → skip.
            var tags = new List<ScrapedTag>();
            var tagType = Str(productTag, "tagType");
            if (!string.IsNullOrWhiteSpace(tagType) && !tagType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                tags.Add(new ScrapedTag(ProductTagSource.Primary, tagType));

            // Multibuy pair ("3 for $20") — published independently of special/club, kept whatever the primary type.
            var multiBuy = Obj(productTag, "multiBuy");
            var multiQty = Dec(multiBuy, "quantity") is { } q and > 1 ? (int?)q : null;
            var multiTotal = multiQty is not null ? Dec(multiBuy, "value") : null;
            if (multiTotal is null) multiQty = null;

            var promoType = isClub ? PromoType.MemberPrice
                : isSpecial ? PromoType.Special
                : multiQty is not null ? PromoType.Multibuy
                : PromoType.None;

            // Price semantics (docs/internals/promotions-model.md): Price = what a cardless shopper pays.
            // Club deal → salePrice is the MEMBER price and originalPrice the shelf price; cupListPrice is the
            // unit price matching the shelf price. Otherwise salePrice IS the shelf price.
            decimal shelfPrice;
            decimal? memberPrice = null, wasPrice = null, unitPrice;
            if (promoType == PromoType.MemberPrice && original is not null)
            {
                shelfPrice = original.Value;
                memberPrice = sale.Value < original.Value ? sale : null;
                unitPrice = Dec(size, "cupListPrice") ?? Dec(size, "cupPrice");
            }
            else
            {
                shelfPrice = sale.Value;
                wasPrice = promoType == PromoType.Special ? original : null;
                unitPrice = Dec(size, "cupPrice");
            }

            into.Add(new ScrapedProduct
            {
                Sku = sku,
                Name = Str(item, "name") ?? sku,
                Brand = Str(item, "brand"),
                Size = Str(size, "volumeSize") ?? Str(size, "packageType"),
                Gtin = Str(item, "barcode"),
                ImageUrl = Str(Obj(item, "images"), "big") ?? Str(Obj(item, "images"), "small"),
                Category = path.Count > 0 ? path[^1].Name : null,
                CategoryPath = path,
                Tags = tags,
                Price = shelfPrice,
                NonSpecialPrice = wasPrice,
                PromoType = promoType,
                MemberPrice = memberPrice,
                MultibuyQuantity = multiQty,
                MultibuyTotal = multiTotal,
                UnitPrice = unitPrice,
                UnitOfMeasure = Str(size, "cupMeasure"),
            });
        }
    }

    /// <summary>name → URL slug: lowercase, drop apostrophes, runs of other non-alphanumerics → single hyphen.</summary>
    internal static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        var prevHyphen = false;
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); prevHyphen = false; }
            else if (ch is '\'' or '’') { /* drop apostrophes */ }
            else if (!prevHyphen && sb.Length > 0) { sb.Append('-'); prevHyphen = true; }
        }
        return sb.ToString().TrimEnd('-');
    }

    // --- JSON helpers ---
    private static JsonElement Obj(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) ? v : default;

    private static string? Str(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.ToString(), _ => null };
    }

    private static decimal? Dec(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static bool Bool(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
