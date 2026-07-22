using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Application.Crawling;

/// <summary>
/// Per-store crawler seam (plan R2/D2). One implementation per chain (in Zhua.Crawling).
/// M1: Playwright for all 3 stores, parsing the page's intercepted JSON rather than the DOM.
/// </summary>
public interface IStoreCrawler
{
    /// <summary>The chain this crawler handles; the orchestrator picks the crawler by store chain.</summary>
    Chain Chain { get; }

    /// <summary>Fetch the current product list for a physical store (store context = its geolocation),
    /// reporting every coverage gap so the orchestrator can tell a complete scrape from a partial one (D28).</summary>
    Task<ScrapeResult> FetchAsync(Store store, CancellationToken ct = default);
}
