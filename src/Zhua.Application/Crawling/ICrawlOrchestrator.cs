using Zhua.Domain.Enums;

namespace Zhua.Application.Crawling;

/// <summary>Runs one crawl for one store and persists the result (open/close a CrawlRun).</summary>
public interface ICrawlOrchestrator
{
    Task<CrawlRunResult> RunAsync(Guid storeId, CancellationToken ct = default);
}

public sealed record CrawlRunResult(
    Guid CrawlRunId,
    CrawlRunStatus Status,
    int ProductsFound,
    int SnapshotsWritten,
    string? Error);
