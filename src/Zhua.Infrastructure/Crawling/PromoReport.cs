using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Crawling;

/// <summary>
/// The per-run promo-distribution report (Kevin's instruction, 2026-07-19 — docs/internals/promotions-model.md):
/// after every crawl, log how each chain's products split across <see cref="PromoType"/>, with the chain's
/// loyalty-program name, so a promo-mapping regression (e.g. a source signal renamed) is visible in the run log
/// the moment it happens. Worker-side reporting; lives here (not the Api) with the other crawl-pipeline services.
/// </summary>
public static class PromoReport
{
    /// <summary>Formatted report lines (one per log call) over the active stores' current products.</summary>
    public static async Task<IReadOnlyList<string>> BuildAsync(ZhuaDbContext db, CancellationToken ct = default)
    {
        var rows = await db.Products
            .Where(p => p.Store.IsActive)
            .GroupBy(p => new { p.Store.Chain, p.PromoType })
            .Select(g => new { g.Key.Chain, g.Key.PromoType, Count = g.Count() })
            .ToListAsync(ct);

        var lines = new List<string>
        {
            "promo distribution by chain (active stores):",
            $"{"chain",-11} {"program",-20} {"total",7} {"none",7} {"special",7} {"member",7} {"multibuy",8}  promo%",
        };

        var chains = rows.Select(r => r.Chain).Distinct().OrderBy(c => c).ToList();
        foreach (var chain in chains)
            lines.Add(FormatRow(chain.ToString(), chain.LoyaltyProgram() ?? "—",
                Counts(rows.Where(r => r.Chain == chain).Select(r => (r.PromoType, r.Count)))));

        lines.Add(FormatRow("TOTAL", "", Counts(rows.Select(r => (r.PromoType, r.Count)))));
        return lines;
    }

    private static (int Total, int None, int Special, int Member, int Multibuy) Counts(
        IEnumerable<(PromoType Type, int Count)> rows)
    {
        int none = 0, special = 0, member = 0, multibuy = 0;
        foreach (var (type, count) in rows)
        {
            switch (type)
            {
                case PromoType.Special: special += count; break;
                case PromoType.MemberPrice: member += count; break;
                case PromoType.Multibuy: multibuy += count; break;
                default: none += count; break;
            }
        }
        return (none + special + member + multibuy, none, special, member, multibuy);
    }

    private static string FormatRow(string chain, string program,
        (int Total, int None, int Special, int Member, int Multibuy) c)
    {
        var promoPct = c.Total == 0 ? 0 : (int)Math.Round(100.0 * (c.Total - c.None) / c.Total);
        return $"{chain,-11} {program,-20} {c.Total,7} {c.None,7} {c.Special,7} {c.Member,7} {c.Multibuy,8}  {promoPct,4}%";
    }
}
