using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>One crawler execution against one store. Audit/observability trail (plan §4).</summary>
public class CrawlRun
{
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }

    public Store Store { get; set; } = null!;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public CrawlRunStatus Status { get; set; } = CrawlRunStatus.Running;

    public int ProductsFound { get; set; }

    public int SnapshotsWritten { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<PriceSnapshot> PriceSnapshots { get; } = new List<PriceSnapshot>();
}
