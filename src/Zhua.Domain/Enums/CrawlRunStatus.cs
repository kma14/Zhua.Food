namespace Zhua.Domain.Enums;

/// <summary>Lifecycle status of a <see cref="Entities.CrawlRun"/>.</summary>
public enum CrawlRunStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Partial = 4,
}
