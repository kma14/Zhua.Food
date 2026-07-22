using Zhua.Application.Reporting.Dtos;

namespace Zhua.Application.Reporting;

/// <summary>Internal ops reports over the catalogue's match state (D30.1) — not a shopper surface.</summary>
public interface IReportQueries
{
    /// <summary>Per-supermarket count of listings by match status (aggregated / 待审 / 悬空), with a grand-total row.</summary>
    Task<ProductStatusReport> ProductStatusAsync(CancellationToken ct = default);
}
