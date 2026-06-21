namespace Zhua.Domain.Entities;

/// <summary>
/// A product as it appears inside one specific store (raw, as-crawled).
/// Carries a denormalized "current price" for fast reads (plan R4); full history lives in <see cref="PriceSnapshot"/>.
/// </summary>
public class StoreProduct
{
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }

    public Store Store { get; set; } = null!;

    /// <summary>Nullable: matching is async/offline (plan R3) — ingestion never blocks on it.</summary>
    public Guid? CanonicalProductId { get; set; }

    public CanonicalProduct? CanonicalProduct { get; set; }

    /// <summary>The store's own product id/SKU at the source.</summary>
    public required string SourceSku { get; set; }

    public required string RawName { get; set; }

    public string? RawBrand { get; set; }

    public string? RawSize { get; set; }

    /// <summary>Barcode captured at crawl time — feeds canonical matching (plan D9).</summary>
    public string? Gtin { get; set; }

    public string? Url { get; set; }

    public string? ImageUrl { get; set; }

    // --- Denormalized current price (plan R4): refreshed every crawl for fast "right now" queries. ---

    /// <summary>Price paid now (special price if on special, else shelf price).</summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>Regular ("was") price; set when on special (mirrors <see cref="PriceSnapshot.NonSpecialPrice"/>).</summary>
    public decimal? CurrentNonSpecialPrice { get; set; }

    public bool IsOnSpecial { get; set; }

    public decimal? UnitPrice { get; set; }

    public string? UnitOfMeasure { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Refreshed every crawl — liveness signal that distinguishes "unchanged" from "vanished" (plan D3).</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? PriceUpdatedAt { get; set; }

    public ICollection<PriceSnapshot> PriceSnapshots { get; } = new List<PriceSnapshot>();
}
