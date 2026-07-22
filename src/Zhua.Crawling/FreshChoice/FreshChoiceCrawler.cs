using Zhua.Application.Crawling;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Crawling.FreshChoice;

/// <summary>
/// FreshChoice crawler (plan D26) — a fundamentally different transport from the other two: the MyFoodLink
/// storefront <b>server-renders</b> the product data into the category-page HTML (no JSON API, no WAF), so this
/// is plain <see cref="HttpClient"/> + AngleSharp parsing — <b>no Playwright, no browser</b>. Store context is the
/// storefront subdomain in <see cref="Store.ExternalStoreId"/> (e.g. <c>hc</c> → hc.store.freshchoice.co.nz);
/// each FreshChoice store is its own independently-priced storefront. Departments are a hardcoded slug list
/// (like Woolworths); the page sidebar exposes no deeper tree, so v1 category granularity is department-level
/// (the fine-grained tree lives in a CloudFront sidebar JSON with store-specific params — future work, see
/// docs/internals/crawling.md).
/// </summary>
public sealed class FreshChoiceCrawler : IStoreCrawler
{
    public Chain Chain => Chain.FreshChoice;

    /// <summary>M1 department slugs + display names (recon 2026-07-19). "deli" currently 404s on the Hauraki
    /// Corner storefront (linked in its sidebar but empty) — kept so it lights up if the store fills it; a 404
    /// department is skipped, never a crawl failure.</summary>
    private static readonly (string Slug, string Name)[] Departments =
    [
        ("meat", "Meat"),
        ("seafood", "Seafood"),
        ("fruit-vegetables", "Fruit & Vegetables"),
        ("dairy-eggs", "Dairy & Eggs"),
        ("deli", "Deli"),
    ];

    private static readonly TimeSpan PerRequestDelay = TimeSpan.FromMilliseconds(750); // politeness (D6)

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) zhua.food-price-tracker");
        return http;
    }

    public async Task<ScrapeResult> FetchAsync(Store store, CancellationToken ct = default)
    {
        var subdomain = store.ExternalStoreId;
        if (string.IsNullOrWhiteSpace(subdomain))
            throw new InvalidOperationException(
                $"FreshChoice store '{store.Name}' needs ExternalStoreId = its storefront subdomain (e.g. \"hc\").");

        var baseUrl = $"https://{subdomain}.store.freshchoice.co.nz";
        var archive = RawCrawlArchive.FromEnvironment(Chain.ToString(), DateTimeOffset.UtcNow);
        var results = new List<ScrapedProduct>();
        var gaps = new List<string>();

        foreach (var (slug, name) in Departments)
        {
            ct.ThrowIfCancellationRequested();
            var path = new[] { new ScrapedCategoryNode(CategoryKind.Department, slug, slug, name) };

            var pagePath = $"/category/{slug}?page=1";
            var pageNo = 1;
            while (pagePath is not null)
            {
                var html = await GetPageAsync(baseUrl + pagePath, ct);
                if (html is null) // 404 — department linked in the sidebar but empty; by design, not a gap
                {
                    Console.WriteLine($"  [freshchoice] {slug}: 404 — department empty/unavailable, skipping");
                    break;
                }
                if (html.Length == 0) // transient failure that survived the retries
                {
                    gaps.Add($"{slug}: page {pageNo} failed after retries");
                    break;
                }

                await archive.SaveAsync($"{slug}_p{pageNo}", html, ct, extension: "html"); // raw archive (D12)

                var (products, next) = FreshChoiceParser.ParsePage(html, path, baseUrl);
                results.AddRange(products);

                pagePath = next;
                pageNo++;
                await Task.Delay(PerRequestDelay, ct);
            }
        }

        return new ScrapeResult(results, gaps);
    }

    /// <summary>One page with retry (plan D28): null = 404 (permanently absent), "" = failed after retries.</summary>
    private static async Task<string?> GetPageAsync(string url, CancellationToken ct)
    {
        var delays = new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8) };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var resp = await Http.GetAsync(url, ct);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // transient network/timeout — fall through to retry
            }
            if (attempt >= delays.Length) return "";
            await Task.Delay(delays[attempt], ct);
        }
    }
}
