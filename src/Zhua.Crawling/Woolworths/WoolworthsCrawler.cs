using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Zhua.Application.Ingestion;
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
    private static readonly string[] DepartmentSlugs = ["meat-poultry", "fruit-veg", "fish-seafood"];

    private const int PageSize = 48;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/131.0.0.0 Safari/537.36 zhua.food-price-tracker";

    public async Task<IReadOnlyList<ScrapedProduct>> FetchAsync(Store store, CancellationToken ct = default)
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
        var (dumpDir, retention) = DumpConfig();
        var archive = new RawCrawlArchive(Chain.ToString(), dumpDir, retention, DateTimeOffset.UtcNow);

        // Warm up the homepage to establish session/store cookies.
        try
        {
            await page.GotoAsync("https://www.woolworths.co.nz",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            await page.WaitForTimeoutAsync(2_000);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { }

        var results = new List<ScrapedProduct>();

        foreach (var deptSlug in DepartmentSlugs)
        {
            ct.ThrowIfCancellationRequested();

            var deptFilters = new List<(CategoryKind Kind, string Slug)> { (CategoryKind.Department, deptSlug) };
            var deptJson = await FetchBrowseAsync(page, archive, deptFilters, 1, ct);
            if (deptJson is null) continue;

            string[] aisleNames;
            using (var doc = JsonDocument.Parse(deptJson))
                aisleNames = FacetNames(doc.RootElement, "Aisle");

            foreach (var aisleName in aisleNames)
            {
                ct.ThrowIfCancellationRequested();
                var aisleFilters = new List<(CategoryKind Kind, string Slug)>(deptFilters) { (CategoryKind.Aisle, Slugify(aisleName)) };
                var aisleJson = await FetchBrowseAsync(page, archive, aisleFilters, 1, ct);
                if (aisleJson is null) continue;

                string[] shelfNames;
                using (var doc = JsonDocument.Parse(aisleJson))
                    shelfNames = FacetNames(doc.RootElement, "Shelf");

                if (shelfNames.Length == 0)
                {
                    await CrawlLeafAsync(page, archive, aisleFilters, aisleJson, results, ct); // aisle is the leaf
                }
                else
                {
                    foreach (var shelfName in shelfNames)
                    {
                        ct.ThrowIfCancellationRequested();
                        var shelfFilters = new List<(CategoryKind Kind, string Slug)>(aisleFilters) { (CategoryKind.Shelf, Slugify(shelfName)) };
                        await CrawlLeafAsync(page, archive, shelfFilters, null, results, ct);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>Crawls one leaf category across all pages, appending products tagged with their full category path.</summary>
    private static async Task CrawlLeafAsync(
        IPage page, RawCrawlArchive archive, List<(CategoryKind Kind, string Slug)> filters, string? firstPageJson, List<ScrapedProduct> results, CancellationToken ct)
    {
        var json = firstPageJson ?? await FetchBrowseAsync(page, archive, filters, 1, ct);
        if (json is null) return;

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
            if (more is null) break;
            using var doc = JsonDocument.Parse(more);
            ParseProductsInto(doc.RootElement, path, results);
        }
    }

    /// <summary>Calls the browse products API from within the page (cookies + x-requested-with). Returns body or null.</summary>
    private static async Task<string?> FetchBrowseAsync(
        IPage page, RawCrawlArchive archive, List<(CategoryKind Kind, string Slug)> filters, int pageNo, CancellationToken ct)
    {
        var sb = new StringBuilder("https://www.woolworths.co.nz/api/v1/products?");
        foreach (var (kind, slug) in filters)
            sb.Append("dasFilter=").Append(kind).Append("%3B%3B").Append(Uri.EscapeDataString(slug)).Append("%3Bfalse&");
        sb.Append("target=browse&inStockProductsOnly=false&size=").Append(PageSize).Append("&page=").Append(pageNo);

        try
        {
            var res = await page.EvaluateAsync<string>(
                "async (u) => { const r = await fetch(u, { headers: { 'x-requested-with': 'OnlineShopping.WebApp' }, credentials: 'include' }); return r.ok ? await r.text() : ''; }",
                sb.ToString());
            await page.WaitForTimeoutAsync(400); // polite spacing (plan D6)
            if (string.IsNullOrEmpty(res)) return null;

            // Archive the raw response for retrospective debugging (plan D12).
            var name = string.Join("__", filters.Select(f => f.Slug)) + $"_p{pageNo}";
            await archive.SaveAsync(name, res, ct);
            return res;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Raw-archive config (plan D12). Default ON to <c>{cwd}/crawl-archive</c> with 7-day retention.
    /// Override dir with <c>ZHUA_CRAWL_DUMP_DIR</c>, retention with <c>ZHUA_CRAWL_DUMP_RETENTION_DAYS</c>,
    /// or disable entirely with <c>ZHUA_CRAWL_DUMP=0</c> (also: off/false/no).
    /// </summary>
    private static (string? Dir, TimeSpan Retention) DumpConfig()
    {
        var flag = Environment.GetEnvironmentVariable("ZHUA_CRAWL_DUMP");
        if (flag is "0" or "off" or "false" or "no" or "OFF" or "FALSE" or "NO")
            return (null, TimeSpan.Zero);

        var dir = Environment.GetEnvironmentVariable("ZHUA_CRAWL_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Directory.GetCurrentDirectory(), "crawl-archive");

        var days = 7;
        if (int.TryParse(Environment.GetEnvironmentVariable("ZHUA_CRAWL_DUMP_RETENTION_DAYS"), out var d) && d > 0)
            days = d;

        return (dir, TimeSpan.FromDays(days));
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
            var sale = Dec(price, "salePrice") ?? Dec(price, "originalPrice");
            if (sale is null) continue;
            var isSpecial = Bool(price, "isSpecial");

            // Promo tag (plan D13): the primary badge. "Other" means no real promo → skip.
            var tags = new List<ScrapedTag>();
            var tagType = Str(Obj(item, "productTag"), "tagType");
            if (!string.IsNullOrWhiteSpace(tagType) && !tagType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                tags.Add(new ScrapedTag(ProductTagSource.Primary, tagType));

            into.Add(new ScrapedProduct
            {
                SourceSku = sku,
                Name = Str(item, "name") ?? sku,
                Brand = Str(item, "brand"),
                Size = Str(size, "volumeSize") ?? Str(size, "packageType"),
                Gtin = Str(item, "barcode"),
                ImageUrl = Str(Obj(item, "images"), "big") ?? Str(Obj(item, "images"), "small"),
                Category = path.Count > 0 ? path[^1].Name : null,
                CategoryPath = path,
                Tags = tags,
                Price = sale.Value,
                NonSpecialPrice = isSpecial ? Dec(price, "originalPrice") : null,
                IsOnSpecial = isSpecial,
                UnitPrice = Dec(size, "cupPrice"),
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
