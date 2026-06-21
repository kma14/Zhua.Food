using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zhua.Application.Ingestion;
using Zhua.Crawling.Woolworths;
using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

var conn = builder.Configuration.GetConnectionString("Default")
           ?? "Host=localhost;Port=5433;Database=zhua;Username=zhua;Password=zhua";
builder.Services.AddInfrastructure(conn);

// Crawler implementations (composition root). One per chain; the orchestrator picks by Store.Chain.
builder.Services.AddSingleton<IStoreCrawler, WoolworthsCrawler>();

var host = builder.Build();

// R7 manual one-shot crawl. Usage: Zhua.Worker crawl [--store woolworths|newworld|paknsave|<guid>]
// (Scheduled mode with Quartz arrives in Phase 2; this CLI never lives in the query Api — plan D5/D8.)
if (args.Length > 0 && args[0].Equals("crawl", StringComparison.OrdinalIgnoreCase))
{
    string? filter = null;
    for (var i = 1; i < args.Length - 1; i++)
        if (args[i] is "--store" or "-s") filter = args[i + 1];

    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ZhuaDbContext>();
    var orchestrator = scope.ServiceProvider.GetRequiredService<ICrawlOrchestrator>();

    var stores = await db.Stores.Where(s => s.IsActive).ToListAsync();
    if (filter is not null)
    {
        stores = stores
            .Where(s => s.Id.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase)
                        || s.Chain.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    if (stores.Count == 0)
    {
        Console.WriteLine($"No active store matched '{filter}'.");
        return;
    }

    foreach (var store in stores)
    {
        Console.WriteLine($"[crawl] {store.Name} ({store.Chain}) ...");
        var r = await orchestrator.RunAsync(store.Id);
        Console.WriteLine(
            $"[crawl] {store.Name}: {r.Status}; products={r.ProductsFound}, snapshots={r.SnapshotsWritten}"
            + (r.Error is null ? "" : $"; error={r.Error}"));
    }

    return;
}

Console.WriteLine("Usage: Zhua.Worker crawl [--store woolworths|newworld|paknsave|<guid>]");
Console.WriteLine("(Scheduled mode arrives in Phase 2.)");
