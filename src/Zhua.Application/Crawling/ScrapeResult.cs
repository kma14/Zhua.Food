namespace Zhua.Application.Crawling;

/// <summary>
/// What one crawler run brought back: the products plus every known coverage gap (plan D28). A gap is a
/// human-readable note ("Frozen: page 3/16 failed after retries") recorded wherever the crawler had to give
/// up on a page/category instead of silently skipping it. An empty gap list is the crawler's claim that the
/// scrape covered the store's full catalog — the orchestrator only reconciles missing products (and records
/// the run as Succeeded rather than Partial) on that claim.
/// </summary>
public sealed record ScrapeResult(IReadOnlyList<ScrapedProduct> Products, IReadOnlyList<string> Gaps)
{
    public bool IsComplete => Gaps.Count == 0;

    public static ScrapeResult Complete(IReadOnlyList<ScrapedProduct> products) => new(products, []);
}
