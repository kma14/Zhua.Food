using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Zhua.Application.Ingestion;
using Zhua.Application.Matching;
using Zhua.Crawling.Foodstuffs;
using Zhua.Crawling.Woolworths;
using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

var conn = builder.Configuration.GetConnectionString("Default") ?? DbDefaults.DevConnectionString;
builder.Services.AddPersistence(conn).AddIngestion().AddMatching();

// Crawler implementations (composition root). One per chain; the orchestrator picks by Store.Chain.
builder.Services.AddSingleton<IStoreCrawler, WoolworthsCrawler>();
builder.Services.AddSingleton<IStoreCrawler, NewWorldCrawler>();
builder.Services.AddSingleton<IStoreCrawler, PaknSaveCrawler>();

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

// R3 offline canonical matching (plan D9/D18). Re-runnable after crawls. Usage: Zhua.Worker match
if (args.Length > 0 && args[0].Equals("match", StringComparison.OrdinalIgnoreCase))
{
    using var scope = host.Services.CreateScope();
    var matcher = scope.ServiceProvider.GetRequiredService<ICanonicalMatcher>();
    Console.WriteLine("[match] canonical matching ...");
    var r = await matcher.RunAsync();
    Console.WriteLine($"[match] canonicals={r.CanonicalProducts}, auto-linked store-products={r.AutoLinked}, "
        + $"pending review={r.PendingReview}, already decided={r.AlreadyDecided}");
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

Console.WriteLine("Usage: Zhua.Worker crawl [--store woolworths|newworld|paknsave|<guid>]");
Console.WriteLine("(Scheduled mode arrives in Phase 2.)");
