using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Zhua.Application.Ingestion;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.Woolworths;

/// <summary>
/// Woolworths NZ crawler (plan D2): drives a real Chromium via Playwright, sets the physical store by
/// geolocation, runs searches for the M1 product types, and parses the intercepted <c>/api/v1/products</c>
/// JSON (not the DOM). Set env <c>ZHUA_CRAWL_DUMP</c> to a folder to also save raw responses for fixtures.
/// </summary>
public sealed class WoolworthsCrawler : IStoreCrawler
{
    public Chain Chain => Chain.Woolworths;

    // M1 common grocery types (plan §1) used as search terms.
    private static readonly string[] SearchTerms =
    [
        "milk", "eggs", "bread", "bananas", "apples", "chicken breast", "beef mince",
        "pork belly", "rice", "cooking oil", "noodles", "soy sauce", "butter", "cheese", "yoghurt",
    ];

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/131.0.0.0 Safari/537.36 zhua.food-price-tracker";

    public async Task<IReadOnlyList<ScrapedProduct>> FetchAsync(Store store, CancellationToken ct = default)
    {
        var dumpDir = Environment.GetEnvironmentVariable("ZHUA_CRAWL_DUMP");

        using var pw = await Playwright.CreateAsync();
        await using var browser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Headless by default; set ZHUA_CRAWL_HEADED to open a visible Chrome window (debugging).
            Headless = Environment.GetEnvironmentVariable("ZHUA_CRAWL_HEADED") is null,
            // --disable-http2 avoids intermittent ERR_HTTP2_PROTOCOL_ERROR from the WAF; the second flag reduces headless fingerprinting.
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

        // Diagnostic (only when dumping): log every response URL+status so we can find the real products endpoint.
        if (dumpDir is not null)
        {
            Directory.CreateDirectory(dumpDir);
            var logPath = Path.Combine(dumpDir, "_responses.log");
            page.Response += (_, r) =>
            {
                try { File.AppendAllText(logPath, $"{r.Status} {r.Url}{Environment.NewLine}"); }
                catch { /* diagnostic only */ }
            };
        }

        // Warm up: load the homepage first so session/store cookies are established before searching.
        try
        {
            await page.GotoAsync("https://www.woolworths.co.nz",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
            await page.WaitForTimeoutAsync(1500);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Non-fatal — continue to searches.
        }

        var results = new Dictionary<string, ScrapedProduct>(StringComparer.Ordinal);

        foreach (var term in SearchTerms)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"https://www.woolworths.co.nz/shop/searchproducts?search={Uri.EscapeDataString(term)}";
            try
            {
                var response = await page.RunAndWaitForResponseAsync(
                    () => page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 }),
                    r => r.Url.Contains("/api/v1/products", StringComparison.OrdinalIgnoreCase) && r.Status == 200,
                    new PageRunAndWaitForResponseOptions { Timeout = 30_000 });

                var body = await response.TextAsync();

                if (dumpDir is not null)
                {
                    Directory.CreateDirectory(dumpDir);
                    var safe = term.Replace(' ', '-');
                    await File.WriteAllTextAsync(Path.Combine(dumpDir, $"woolworths-{safe}.json"), body, ct);
                }

                ParseInto(body, term, results);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                // This term failed (timeout / navigation error) — skip it, keep crawling the rest.
            }

            await page.WaitForTimeoutAsync(800); // polite spacing between searches (plan D6)
        }

        return results.Values.ToList();
    }

    /// <summary>Parses a Woolworths <c>/api/v1/products</c> JSON body into the shared dict (keyed by SKU). Testable.</summary>
    internal static void ParseInto(string json, string? category, Dictionary<string, ScrapedProduct> into)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("products", out var products)) return;
        if (!products.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("type", out var type) &&
                !string.Equals(type.GetString(), "Product", StringComparison.OrdinalIgnoreCase))
                continue;

            var sku = Str(item, "sku");
            if (string.IsNullOrWhiteSpace(sku)) continue;

            var price = Obj(item, "price");
            var size = Obj(item, "size");

            var sale = Dec(price, "salePrice") ?? Dec(price, "originalPrice");
            if (sale is null) continue; // no usable price

            var isSpecial = Bool(price, "isSpecial");

            into[sku] = new ScrapedProduct
            {
                SourceSku = sku,
                Name = Str(item, "name") ?? sku,
                Brand = Str(item, "brand"),
                Size = Str(size, "volumeSize") ?? Str(size, "packageType"),
                Gtin = Str(item, "barcode"),
                ImageUrl = Str(Obj(item, "images"), "big") ?? Str(Obj(item, "images"), "small"),
                Category = category,
                Price = sale.Value,
                NonSpecialPrice = isSpecial ? Dec(price, "originalPrice") : null,
                IsOnSpecial = isSpecial,
                UnitPrice = Dec(size, "cupPrice"),
                UnitOfMeasure = Str(size, "cupMeasure"),
            };
        }
    }

    private static JsonElement Obj(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) ? v : default;

    private static string? Str(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null,
        };
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
