using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>
/// Append-only price-history row. One row per price-tuple change (plan D3) — NOT one per crawl.
/// The price holds from <see cref="CapturedAt"/> until the next change (step-function history).
/// </summary>
public class PriceSnapshot
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>The crawl run that produced this snapshot (observability).</summary>
    public Guid CrawlRunId { get; set; }

    public CrawlRun CrawlRun { get; set; } = null!;

    public decimal Price { get; set; }

    /// <summary>The non-special ("was") price when on special — needed to answer "is it on special?".</summary>
    public decimal? NonSpecialPrice { get; set; }

    /// <summary>Derived: <c>PromoType == Special</c> (narrowed 2026-07-17, docs/internals/promotions-model.md).</summary>
    public bool IsOnSpecial { get; set; }

    /// <summary>The primary promotion at capture time (decision B2 — type changes are history-worthy).</summary>
    public PromoType PromoType { get; set; }

    /// <summary>Loyalty-card price at capture time (when <see cref="PromoType"/> is MemberPrice).</summary>
    public decimal? MemberPrice { get; set; }

    public int? MultibuyQuantity { get; set; }

    public decimal? MultibuyTotal { get; set; }

    public decimal? UnitPrice { get; set; }

    public string Currency { get; set; } = "NZD";

    public DateTimeOffset CapturedAt { get; set; }
}
