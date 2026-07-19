using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Quartz;
using Zhua.Application.Crawling;
using Zhua.Application.Matching;
using Zhua.Crawling.Foodstuffs;
using Zhua.Crawling.Woolworths;
using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;
using Zhua.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

var conn = builder.Configuration.GetConnectionString("Default") ?? DbDefaults.DevConnectionString;
builder.Services.AddPersistence(conn).AddCrawling().AddMatching();

// Crawler implementations (composition root). One per chain; the orchestrator picks by Store.Chain.
builder.Services.AddSingleton<IStoreCrawler, WoolworthsCrawler>();
builder.Services.AddSingleton<IStoreCrawler, NewWorldCrawler>();
builder.Services.AddSingleton<IStoreCrawler, PaknSaveCrawler>();

// No CLI command = scheduled mode (plan D4/D7): Quartz fires the crawling job on a cron (default twice-daily).
var scheduled = args.Length == 0;
if (scheduled)
{
    var cron = builder.Configuration["Crawl:Cron"] ?? "0 0 6,18 * * ?"; // 06:00 & 18:00 local, daily
    builder.Services.AddQuartz(q =>
    {
        var job = new JobKey("crawling");
        q.AddJob<CrawlJob>(job);
        q.AddTrigger(t => t.ForJob(job).WithIdentity("crawling-trigger")
            .WithCronSchedule(cron, x => x.InTimeZone(TimeZoneInfo.Local)));
    });
    builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
}

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

    // Per-run promo-distribution report (docs/internals/promotions-model.md).
    foreach (var line in await Zhua.Infrastructure.Crawling.PromoReport.BuildAsync(db))
        Console.WriteLine($"[report] {line}");

    return;
}

// R3 offline item matching (plan D9/D18). Re-runnable after crawls. Usage: Zhua.Worker match
if (args.Length > 0 && args[0].Equals("match", StringComparison.OrdinalIgnoreCase))
{
    using var scope = host.Services.CreateScope();
    var matcher = scope.ServiceProvider.GetRequiredService<IItemMatcher>();
    Console.WriteLine("[match] item matching ...");
    var r = await matcher.RunAsync();
    Console.WriteLine($"[match] items={r.Items}, auto-linked store-products={r.AutoLinked}, "
        + $"pending review={r.PendingReview}, already decided={r.AlreadyDecided}");

    var categoryMapper = scope.ServiceProvider.GetRequiredService<ICategoryMapper>();
    Console.WriteLine("[match] category mapping ...");
    var cm = await categoryMapper.MapAsync();
    Console.WriteLine($"[match] categories={cm.Categories}, "
        + $"mapped store-categories={cm.MappedStoreCategories}, categorized products={cm.CategorizedProducts}");
    return;
}

// Ad-hoc promo-distribution report over the current DB (the same table every crawl run logs at its end).
// Usage: Zhua.Worker report
if (args.Length > 0 && args[0].Equals("report", StringComparison.OrdinalIgnoreCase))
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ZhuaDbContext>();
    foreach (var line in await Zhua.Infrastructure.Crawling.PromoReport.BuildAsync(db))
        Console.WriteLine($"[report] {line}");
    return;
}

// Throwaway recon probe: open a URL headed and dump every JSON network response (to design new crawlers).
// Usage: Zhua.Worker recon <url>
if (args.Length > 0 && args[0].Equals("recon", StringComparison.OrdinalIgnoreCase))
{
    var url = args.Length > 1 ? args[1]
        : "https://www.newworld.co.nz/shop/category/meat-poultry-and-seafood/beef?pg=1";
    var outDir = Path.Combine(Directory.GetCurrentDirectory(), "recon",
        DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"));
    Directory.CreateDirectory(outDir);

    using var pw = await Playwright.CreateAsync();
    await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = false, Args = ["--disable-http2"] });
    var context = await browser.NewContextAsync(new()
    {
        Geolocation = new() { Latitude = -36.78f, Longitude = 174.75f }, // North Shore (auto-picks nearest store)
        Permissions = ["geolocation"],
        Locale = "en-NZ",
    });
    var page = await context.NewPageAsync();

    var n = 0;
    page.Response += async (_, resp) =>
    {
        try
        {
            var ctype = resp.Headers.TryGetValue("content-type", out var c) ? c : "";
            if (!ctype.Contains("json", StringComparison.OrdinalIgnoreCase)) return;
            var body = await resp.TextAsync();
            var seq = Interlocked.Increment(ref n);
            var seg = new Uri(resp.Url).AbsolutePath.TrimEnd('/');
            seg = seg[(seg.LastIndexOf('/') + 1)..];
            var safe = new string(seg.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            if (safe.Length > 60) safe = safe[..60];
            var reqBody = resp.Request.PostData;
            var reqHeaders = await resp.Request.AllHeadersAsync();
            var headerDump = string.Join("\n", reqHeaders.Select(kv => $"//   {kv.Key}: {kv.Value}"));
            var header = $"// {resp.Request.Method} {resp.Url}\n"
                + $"// REQUEST HEADERS:\n{headerDump}\n"
                + (string.IsNullOrEmpty(reqBody) ? "" : $"// REQUEST BODY: {reqBody}\n");
            await File.WriteAllTextAsync(Path.Combine(outDir, $"{seq:D3}_{safe}.json"), header + body);
            Console.WriteLine($"  [{resp.Status}] {resp.Url}");
        }
        catch { /* best-effort recon */ }
    };

    Console.WriteLine($"[recon] {url}");
    try { await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 }); }
    catch (Exception ex) { Console.WriteLine($"[recon] nav: {ex.Message}"); }
    await page.WaitForTimeoutAsync(4_000);
    await browser.CloseAsync();
    Console.WriteLine($"[recon] {n} JSON responses saved to {outDir}");
    return;
}

if (scheduled)
{
    // Scheduled mode: Quartz hosted service runs the cron-driven crawling job until the process is stopped.
    await host.RunAsync();
    return;
}

Console.WriteLine("Usage: Zhua.Worker [<no args> = scheduled crawl+match | crawl [--store <chain>] | match | report | recon <url>]");
