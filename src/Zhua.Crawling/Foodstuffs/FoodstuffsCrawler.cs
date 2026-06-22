using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Zhua.Application.Ingestion;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.Foodstuffs;

/// <summary>
/// Base crawler for Foodstuffs banners — New World &amp; PAK'nSAVE share one platform and API (plan D15), so the
/// only differences are the domain and the store. Resolves the store id, then queries each M1 department's
/// Algolia-backed product search (<c>/v1/edge/search/paginated/products</c>) and tags each product with its
/// embedded <c>categoryTrees</c> (a product carries its full Department→Aisle→Shelf path, often several).
/// Headless is WAF-risky, so headed by default like Woolworths (<c>ZHUA_CRAWL_HEADLESS</c> overrides).
/// </summary>
public abstract class FoodstuffsCrawler : IStoreCrawler
{
    public abstract Chain Chain { get; }

    /// <summary>Storefront origin, e.g. <c>https://www.newworld.co.nz</c>.</summary>
    protected abstract string SiteBaseUrl { get; }

    /// <summary>Edge API origin, e.g. <c>https://api-prod.newworld.co.nz</c>.</summary>
    protected abstract string ApiBaseUrl { get; }

    /// <summary>
    /// M1 department names (category level0) to crawl — shared by both banners (same Foodstuffs taxonomy).
    /// Foodstuffs folds seafood under "Meat, Poultry &amp; Seafood". Override only if a banner ever diverges.
    /// </summary>
    protected virtual string[] DepartmentNames =>
        ["Meat, Poultry & Seafood", "Fruit & Vegetables", "Fridge, Deli & Eggs", "Frozen"];

    private const int HitsPerPage = 50;

    public async Task<IReadOnlyList<ScrapedProduct>> FetchAsync(Store store, CancellationToken ct = default)
    {
        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("ZHUA_CRAWL_HEADLESS") is not null,
            Args = ["--disable-http2", "--disable-blink-features=AutomationControlled"],
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            Geolocation = new Geolocation { Latitude = (float)store.Latitude, Longitude = (float)store.Longitude },
            Permissions = ["geolocation"],
            Locale = "en-NZ",
        });
        var page = await context.NewPageAsync();

        // The edge API requires an anonymous Bearer token the SPA mints and attaches to every api-prod call.
        // Capture it from the page's own requests during warmup, then reuse it for our fetches (plan D15).
        string? bearer = null;
        page.Request += (_, req) =>
        {
            if (bearer is null && req.Url.Contains("api-prod.", StringComparison.OrdinalIgnoreCase)
                && req.Headers.TryGetValue("authorization", out var auth)
                && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                bearer = auth;
        };

        var archive = RawCrawlArchive.FromEnvironment(Chain.ToString(), DateTimeOffset.UtcNow);

        // Warm up the storefront to establish session/store cookies and mint the token.
        try
        {
            await page.GotoAsync(SiteBaseUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { }
        for (var i = 0; i < 20 && bearer is null; i++)
            await page.WaitForTimeoutAsync(500); // wait for the SPA to fire its first authed api-prod call

        // Store id: prefer the seeded ExternalStoreId, else resolve by geolocation (plan D2 — store context = lat/long).
        var storeId = store.ExternalStoreId;
        if (string.IsNullOrWhiteSpace(storeId))
            storeId = await ResolveStoreIdAsync(page, store, archive, ct);
        if (string.IsNullOrWhiteSpace(storeId))
            return [];

        var results = new List<ScrapedProduct>();
        foreach (var dept in DepartmentNames)
        {
            ct.ThrowIfCancellationRequested();
            await CrawlDepartmentAsync(page, archive, bearer, storeId!, dept, results, ct);
        }
        return results;
    }

    /// <summary>Crawls one department across all pages; products self-describe their category path(s).</summary>
    private async Task CrawlDepartmentAsync(
        IPage page, RawCrawlArchive archive, string? bearer, string storeId, string dept, List<ScrapedProduct> results, CancellationToken ct)
    {
        var first = await FetchProductsAsync(page, archive, bearer, storeId, dept, 0, ct);
        if (first is null) return;

        int totalPages;
        using (var doc = JsonDocument.Parse(first))
        {
            totalPages = Int(doc.RootElement, "totalPages") ?? 1;
            ParseProductsInto(doc.RootElement, results);
        }

        for (var p = 1; p < totalPages; p++)
        {
            ct.ThrowIfCancellationRequested();
            var more = await FetchProductsAsync(page, archive, bearer, storeId, dept, p, ct);
            if (more is null) break;
            using var doc = JsonDocument.Parse(more);
            ParseProductsInto(doc.RootElement, results);
        }
    }

    private async Task<string?> ResolveStoreIdAsync(IPage page, Store store, RawCrawlArchive archive, CancellationToken ct)
    {
        var url = $"{SiteBaseUrl}/next/api/stores/geolocation"
            + $"?lat={store.Latitude.ToString(CultureInfo.InvariantCulture)}"
            + $"&lng={store.Longitude.ToString(CultureInfo.InvariantCulture)}";

        var json = await GetAsync(page, url, bearer: null); // geolocation lives on the storefront, no auth needed
        if (json is null) return null;
        await archive.SaveAsync("store-geolocation", json, ct);

        using var doc = JsonDocument.Parse(json);
        var data = Obj(doc.RootElement, "data");
        return Str(data, "id");
    }

    private async Task<string?> FetchProductsAsync(
        IPage page, RawCrawlArchive archive, string? bearer, string storeId, string dept, int pageNo, CancellationToken ct)
    {
        var body = BuildSearchBody(storeId, dept, pageNo);
        var json = await PostAsync(page, $"{ApiBaseUrl}/v1/edge/search/paginated/products", body, bearer);
        await page.WaitForTimeoutAsync(400); // polite spacing (plan D6)
        if (json is null) return null;

        await archive.SaveAsync($"{Slugify(dept)}_p{pageNo}", json, ct);
        return json;
    }

    /// <summary>Mirrors the storefront's Algolia-backed request; filters products by department (category level0 name).</summary>
    private static string BuildSearchBody(string storeId, string dept, int pageNo)
    {
        var query = new
        {
            algoliaQuery = new
            {
                attributesToRetrieve = new[] { "productID", "Type", "sponsored", "category0NI", "category1NI", "category2NI" },
                facets = new[] { "category2NI", "onPromotion" },
                filters = $"stores:{storeId} AND category0NI:\"{dept}\"",
                hitsPerPage = HitsPerPage,
                maxValuesPerFacet = 100,
                page = pageNo,
                analyticsTags = new[] { "fs#WEB:desktop" },
            },
            algoliaFacetQueries = Array.Empty<object>(),
            storeId,
            hitsPerPage = HitsPerPage,
            page = pageNo,
            sortOrder = "NI_POPULARITY_ASC",
            tobaccoQuery = false,
            precisionMedia = new { adDomain = "CATEGORY_PAGE", adPositions = Array.Empty<int>(), publishImpressionEvent = false, disableAds = true },
        };
        return JsonSerializer.Serialize(query);
    }

    /// <summary>Parses a Foodstuffs products response into <see cref="ScrapedProduct"/>s. One per category tree (the
    /// orchestrator dedups by SKU and accumulates categories, plan D11). Testable.</summary>
    internal void ParseProductsInto(JsonElement root, List<ScrapedProduct> into)
    {
        if (!root.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in products.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var sku = Str(item, "productId");
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var single = Obj(item, "singlePrice");
            var priceCents = Int(single, "price");
            if (priceCents is null) continue;
            var price = priceCents.Value / 100m;

            var comp = Obj(single, "comparativePrice");
            var unitPrice = Int(comp, "pricePerUnit") is { } pp ? pp / 100m : (decimal?)null;
            var unitMeasure = Str(comp, "measureDescription") ?? Str(comp, "unitQuantityUom");

            // On special when a promotion is attached (Foodstuffs gives the promo price, not a "was" price → null).
            var hasPromo = item.TryGetProperty("promotions", out var promos)
                && promos.ValueKind == JsonValueKind.Array && promos.GetArrayLength() > 0;

            var tags = new List<ScrapedTag>();
            if (hasPromo)
            {
                var promo = promos.EnumerateArray().First();
                var decal = Str(promo, "decal");
                if (!string.IsNullOrWhiteSpace(decal))
                    tags.Add(new ScrapedTag(ProductTagSource.Primary, decal!, Str(promo, "rewardType")));
            }

            var name = Str(item, "name") ?? sku;
            var brand = Str(item, "brand");
            var size = Str(item, "displayName");

            // Emit one product per category tree that sits under a department we crawl (skip stray cross-dept trees).
            var emitted = false;
            if (item.TryGetProperty("categoryTrees", out var trees) && trees.ValueKind == JsonValueKind.Array)
            {
                foreach (var tree in trees.EnumerateArray())
                {
                    var level0 = Str(tree, "level0");
                    if (level0 is null || !DepartmentNames.Contains(level0)) continue;
                    into.Add(NewProduct(sku!, name, brand, size, price, hasPromo, unitPrice, unitMeasure, BuildPath(tree), tags));
                    emitted = true;
                }
            }

            if (!emitted)
                into.Add(NewProduct(sku!, name, brand, size, price, hasPromo, unitPrice, unitMeasure, [], tags));
        }
    }

    private static ScrapedProduct NewProduct(
        string sku, string name, string? brand, string? size, decimal price, bool isOnSpecial,
        decimal? unitPrice, string? unitOfMeasure, IReadOnlyList<ScrapedCategoryNode> path, IReadOnlyList<ScrapedTag> tags) =>
        new()
        {
            SourceSku = sku,
            Name = name,
            Brand = brand,
            Size = size,
            Gtin = null,        // Foodstuffs search API exposes no barcode (canonical match falls back to brand+name, D9)
            ImageUrl = null,    // …and no image URL here (derive from fsimg CDN later)
            Category = path.Count > 0 ? path[^1].Name : null,
            CategoryPath = path,
            Tags = tags,
            Price = price,
            NonSpecialPrice = null, // no "was" price exposed
            IsOnSpecial = isOnSpecial,
            UnitPrice = unitPrice,
            UnitOfMeasure = unitOfMeasure,
        };

    /// <summary>level0/1/2 → Department/Aisle/Shelf nodes. ExternalId = name (the source has no stable category id).</summary>
    private static IReadOnlyList<ScrapedCategoryNode> BuildPath(JsonElement tree)
    {
        var nodes = new List<ScrapedCategoryNode>(3);
        Add(CategoryKind.Department, Str(tree, "level0"));
        Add(CategoryKind.Aisle, Str(tree, "level1"));
        Add(CategoryKind.Shelf, Str(tree, "level2"));
        return nodes;

        void Add(CategoryKind kind, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                nodes.Add(new ScrapedCategoryNode(kind, name!, Slugify(name!), name!));
        }
    }

    // --- page-context fetch (uses the site's cookies + the SPA's Bearer token; api-prod allows the call the site itself makes) ---
    private static async Task<string?> GetAsync(IPage page, string url, string? bearer)
    {
        try
        {
            var res = await page.EvaluateAsync<string>(
                "async (a) => { const h = {}; if (a.t) h['authorization'] = a.t; const r = await fetch(a.u, { headers: h, credentials: 'include' }); return r.ok ? await r.text() : ''; }",
                new { u = url, t = bearer });
            return string.IsNullOrEmpty(res) ? null : res;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { return null; }
    }

    private static async Task<string?> PostAsync(IPage page, string url, string body, string? bearer)
    {
        try
        {
            var res = await page.EvaluateAsync<string>(
                "async (a) => { const h = { 'content-type': 'application/json' }; if (a.t) h['authorization'] = a.t; const r = await fetch(a.u, { method: 'POST', headers: h, body: a.b, credentials: 'include' }); return r.ok ? await r.text() : ''; }",
                new { u = url, b = body, t = bearer });
            return string.IsNullOrEmpty(res) ? null : res;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { return null; }
    }

    /// <summary>name → slug (cosmetic only — Foodstuffs is queried by name, not slug).</summary>
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

    private static int? Int(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }
}
