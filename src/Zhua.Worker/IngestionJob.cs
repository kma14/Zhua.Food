using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using Zhua.Application.Ingestion;
using Zhua.Application.Matching;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Worker;

/// <summary>
/// The scheduled ingestion job (plan D4/D7): crawl every active store sequentially and politely, then run the
/// canonical matcher so new products self-sort into auto-matched / review. <see cref="DisallowConcurrentExecutionAttribute"/>
/// stops a slow run from overlapping the next trigger.
/// </summary>
[DisallowConcurrentExecution]
public sealed class IngestionJob(
    ZhuaDbContext db,
    ICrawlOrchestrator orchestrator,
    ICanonicalMatcher matcher,
    ILogger<IngestionJob> log) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        var stores = await db.Stores.Where(s => s.IsActive).OrderBy(s => s.Chain).ThenBy(s => s.Name).ToListAsync(ct);
        log.LogInformation("[scheduled] starting crawl of {Count} active stores", stores.Count);

        foreach (var store in stores)
        {
            ct.ThrowIfCancellationRequested();
            var r = await orchestrator.RunAsync(store.Id, ct);
            log.LogInformation("[scheduled] {Store}: {Status} products={Products} snapshots={Snapshots}{Error}",
                store.Name, r.Status, r.ProductsFound, r.SnapshotsWritten, r.Error is null ? "" : $" error={r.Error}");
        }

        var m = await matcher.RunAsync(ct);
        log.LogInformation("[scheduled] match: canonicals={Canonicals} linked={Linked} pendingReview={Pending}",
            m.CanonicalProducts, m.AutoLinked, m.PendingReview);
    }
}
