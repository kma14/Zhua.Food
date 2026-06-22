using System.Text.RegularExpressions;

namespace Zhua.Crawling;

/// <summary>
/// Archives raw crawler responses to disk for retrospective debugging (plan D12). Each run gets its own
/// timestamped folder under <c>{root}/{chain}/</c>; on construction we prune run folders older than the
/// retention window (default 7 days) so the archive self-cleans. Disabled = a no-op instance (no files written).
/// </summary>
public sealed partial class RawCrawlArchive
{
    private readonly string? _runDir;

    /// <param name="chain">Store chain, used as the per-chain sub-folder.</param>
    /// <param name="rootDir">Archive root; null/empty disables archiving (no-op).</param>
    /// <param name="retention">Run folders older than this are pruned at startup.</param>
    /// <param name="now">Run timestamp (UTC), names the run folder.</param>
    public RawCrawlArchive(string chain, string? rootDir, TimeSpan retention, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(rootDir)) return; // archiving disabled → no-op

        var chainRoot = Path.Combine(rootDir, chain);
        Directory.CreateDirectory(chainRoot);
        Prune(chainRoot, now - retention);

        _runDir = Path.Combine(chainRoot, now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"));
        Directory.CreateDirectory(_runDir);
    }

    /// <summary>True when archiving is active (root configured).</summary>
    public bool IsEnabled => _runDir is not null;

    /// <summary>The folder this run writes to, or null when disabled.</summary>
    public string? RunDir => _runDir;

    /// <summary>Writes one raw response. <paramref name="name"/> is sanitised into a safe file name.</summary>
    public async Task SaveAsync(string name, string content, CancellationToken ct = default)
    {
        if (_runDir is null) return;
        var file = Path.Combine(_runDir, Sanitize(name) + ".json");
        await File.WriteAllTextAsync(file, content, ct);
    }

    /// <summary>Deletes run folders last written before <paramref name="cutoff"/>. Best-effort; ignores IO races.</summary>
    private static void Prune(string chainRoot, DateTimeOffset cutoff)
    {
        foreach (var dir in Directory.EnumerateDirectories(chainRoot))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff.UtcDateTime)
                    Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string Sanitize(string name)
    {
        var cleaned = InvalidChars().Replace(name, "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "response" : cleaned;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex InvalidChars();
}
