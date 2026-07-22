using Zhua.Application.Reporting.Dtos;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Reporting;

/// <summary>
/// Builds the product-status report (D30.1). The DB counts listings by (chain, anchor scheme, has-pending) via
/// <see cref="IProductRepository.CountByMatchStatusAsync"/>; this maps each anchor scheme (the mechanical MatchKey
/// prefix set by <c>ItemMatcher</c>) to a status column and folds the buckets into one row per supermarket + a total.
/// No EF here — the scheme → status classification is application logic (docs/internals/glossary.md).
/// </summary>
public sealed class ReportQueries(IProductRepository products) : IReportQueries
{
    // The MatchKey schemes ItemMatcher stamps (matching.md / glossary.md). Kept here (not shared consts) so the
    // report reads standalone; if the matcher's prefixes ever change, this mapping changes with them.
    private const string Foodstuffs = "foodstuffs";
    private const string Woolworths = "woolworths";
    private const string FreshChoice = "freshchoice";

    // The supermarkets, in a stable display order — every one gets a row even at zero.
    private static readonly Chain[] Order = [Chain.NewWorld, Chain.PaknSave, Chain.Woolworths, Chain.FreshChoice];

    public async Task<ProductStatusReport> ProductStatusAsync(CancellationToken ct = default)
    {
        var counts = await products.CountByMatchStatusAsync(ct);
        var acc = Order.ToDictionary(c => c, _ => new Accumulator());

        foreach (var row in counts)
        {
            if (!acc.TryGetValue(row.Chain, out var a)) continue; // ignore any inactive/unknown chain
            a.Add(row.AnchorScheme, row.HasPendingCandidate, row.Count);
        }

        var rows = Order.Select(c => acc[c].ToRow(c.ToString())).ToList();
        var total = rows.Aggregate(new Accumulator(), (t, r) => t.AddRow(r)).ToRow("Total");
        return new ProductStatusReport(rows, total);
    }

    /// <summary>Mutable per-chain tally; one place that knows scheme → status so the columns always sum to the total.</summary>
    private sealed class Accumulator
    {
        private int _fs, _ww, _fc, _manual, _pending, _held;

        public void Add(string? scheme, bool hasPending, int count)
        {
            switch (scheme)
            {
                case Foodstuffs: _fs += count; break;
                case Woolworths: _ww += count; break;
                case FreshChoice: _fc += count; break;
                case null: if (hasPending) _pending += count; else _held += count; break;
                default: _manual += count; break; // manual: + any other linked scheme → "other linked"
            }
        }

        public Accumulator AddRow(ChainStatusRow r)
        {
            _fs += r.FoodstuffsItem; _ww += r.WoolworthsItem; _fc += r.FreshChoiceItem;
            _manual += r.ManualItem; _pending += r.PendingReview; _held += r.Held;
            return this;
        }

        public ChainStatusRow ToRow(string supermarket) => new(
            supermarket, _fs, _ww, _fc, _manual, _pending, _held,
            _fs + _ww + _fc + _manual + _pending + _held);
    }
}
