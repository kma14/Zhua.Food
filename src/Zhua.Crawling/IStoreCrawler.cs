using Zhua.Domain.Enums;

namespace Zhua.Crawling;

/// <summary>
/// Per-store crawler seam (plan R2). Each store implements this. M1 uses Playwright for all 3,
/// intercepting the page's JSON rather than scraping the DOM (plan D2). The fetch contract is defined in Phase 1.
/// </summary>
public interface IStoreCrawler
{
    Chain Chain { get; }
}
