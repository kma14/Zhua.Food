using Zhua.Domain.Enums;
using Zhua.Domain.ValueObjects;

namespace Zhua.Domain.Entities;

/// <summary>
/// A product as it appears inside one specific store (raw, as-crawled).
/// Carries a denormalized "current price" for fast reads (plan R4); full history lives in <see cref="PriceSnapshot"/>.
/// </summary>
public class Product
{
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }

    public Store Store { get; set; } = null!;

    /// <summary>Nullable: matching is async/offline (plan R3) — crawling never blocks on it.</summary>
    public Guid? ItemId { get; set; }

    public Item? Item { get; set; }

    /// <summary>Store categories this product appears under (many-to-many; plan D11) — a product sits in several shelves.</summary>
    public ICollection<StoreCategory> Categories { get; } = new List<StoreCategory>();

    /// <summary>Promo/marketing tags currently on this product (many-to-many; plan D13). Reset every crawl — current state only, not history.</summary>
    public ICollection<ProductTag> Tags { get; } = new List<ProductTag>();

    /// <summary>The store's own product id/SKU at the source.</summary>
    public required string Sku { get; set; }

    public required string RawName { get; set; }

    public string? RawBrand { get; set; }

    public string? RawSize { get; set; }

    /// <summary>Barcode captured at crawl time — feeds item matching (plan D9).</summary>
    public string? Gtin { get; set; }

    public string? Url { get; set; }

    public string? ImageUrl { get; set; }

    // --- Denormalized current price (plan R4): refreshed every crawl for fast "right now" queries. ---

    /// <summary>Price paid now (special price if on special, else shelf price).</summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>Regular ("was") price; set when on special (mirrors <see cref="PriceSnapshot.NonSpecialPrice"/>).</summary>
    public decimal? CurrentNonSpecialPrice { get; set; }

    /// <summary>Derived: <c>PromoType == Special</c> (narrowed 2026-07-17 — member/multibuy no longer count;
    /// docs/internals/promotions-model.md). Kept as a stored column so the deals predicate stays a plain filter.</summary>
    public bool IsOnSpecial { get; set; }

    /// <summary>The primary promotion currently on this listing (decision D1: one type, precedence
    /// MemberPrice &gt; Special &gt; Multibuy — docs/internals/promotions-model.md).</summary>
    public PromoType PromoType { get; set; }

    /// <summary>Loyalty-card price when <see cref="PromoType"/> is MemberPrice; <see cref="CurrentPrice"/> stays
    /// the non-member shelf price.</summary>
    public decimal? MemberPrice { get; set; }

    /// <summary>Multibuy "N for $X": N. Captured whenever the source publishes one, whatever the primary type.</summary>
    public int? MultibuyQuantity { get; set; }

    /// <summary>Multibuy "N for $X": the total $X (not a unit price).</summary>
    public decimal? MultibuyTotal { get; set; }

    public decimal? UnitPrice { get; set; }

    public string? UnitOfMeasure { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Refreshed every crawl — liveness signal that distinguishes "unchanged" from "vanished" (plan D3).</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? PriceUpdatedAt { get; set; }

    public ICollection<PriceSnapshot> PriceSnapshots { get; } = new List<PriceSnapshot>();

    /// <summary>
    /// Applies one crawl observation and owns the change-only price rule (plan D3): always refreshes the
    /// denormalized current fields + <see cref="LastSeenAt"/> (R4), and appends a <see cref="PriceSnapshot"/>
    /// ONLY when the price tuple <c>{Price, PromoType, NonSpecialPrice, UnitPrice, MemberPrice, Multibuy pair}</c>
    /// changed (a first observation of a new product counts as a change; comparing PromoType subsumes the old
    /// IsOnSpecial term). Returns the new snapshot, or null if nothing changed.
    /// The caller links the snapshot to its <see cref="CrawlRun"/> (an orchestration concern).
    /// </summary>
    public PriceSnapshot? ApplyObservation(ProductObservation obs, DateTimeOffset now)
    {
        var isOnSpecial = obs.PromoType == PromoType.Special;
        var nonSpecialPrice = ReconstructWasPrice(obs, isOnSpecial);

        var priceChanged =
            CurrentPrice != obs.Price
            || PromoType != obs.PromoType
            || CurrentNonSpecialPrice != nonSpecialPrice
            || UnitPrice != obs.UnitPrice
            || MemberPrice != obs.MemberPrice
            || MultibuyQuantity != obs.MultibuyQuantity
            || MultibuyTotal != obs.MultibuyTotal;

        // Always refresh raw fields + denormalized current price + liveness (plan R4 / D3).
        RawName = obs.Name;
        RawBrand = obs.Brand;
        RawSize = obs.Size;
        Gtin = obs.Gtin;
        Url = obs.Url;
        ImageUrl = obs.ImageUrl;
        CurrentPrice = obs.Price;
        CurrentNonSpecialPrice = nonSpecialPrice;
        IsOnSpecial = isOnSpecial;
        PromoType = obs.PromoType;
        MemberPrice = obs.MemberPrice;
        MultibuyQuantity = obs.MultibuyQuantity;
        MultibuyTotal = obs.MultibuyTotal;
        UnitPrice = obs.UnitPrice;
        UnitOfMeasure = obs.UnitOfMeasure;
        LastSeenAt = now;

        if (!priceChanged) return null;

        PriceUpdatedAt = now;
        var snapshot = new PriceSnapshot
        {
            Price = obs.Price,
            NonSpecialPrice = nonSpecialPrice,
            IsOnSpecial = isOnSpecial,
            PromoType = obs.PromoType,
            MemberPrice = obs.MemberPrice,
            MultibuyQuantity = obs.MultibuyQuantity,
            MultibuyTotal = obs.MultibuyTotal,
            UnitPrice = obs.UnitPrice,
            CapturedAt = now,
        };
        PriceSnapshots.Add(snapshot);
        return snapshot;
    }

    /// <summary>
    /// The regular ("was") price to record for this observation. Prefer the source's own value (Woolworths
    /// publishes it). Foodstuffs (NW/PAK) flags a special but publishes NO was-price, so we reconstruct it from
    /// our own prior state (D23): the shelf price we last saw before the product went on special. This is
    /// <b>going-forward only</b> — we can't recover a was-price for a special that was already running the first
    /// time we saw the product (prior state unknown). The guard <c>prior &gt; obs.Price</c> avoids fabricating a
    /// non-discount (zero/negative saving) from noisy data. Applies to public specials only: a MemberPrice
    /// product's <see cref="CurrentPrice"/> is the undiscounted shelf price, so there is nothing to reconstruct
    /// (docs/internals/promotions-model.md, correction 2026-07-17).
    /// </summary>
    private decimal? ReconstructWasPrice(ProductObservation obs, bool isOnSpecial)
    {
        if (!isOnSpecial || obs.NonSpecialPrice is not null)
            return obs.NonSpecialPrice;

        if (!IsOnSpecial && CurrentPrice is { } prior && prior > obs.Price)
            return prior;                    // just went on special: the prior shelf price is the "was"

        if (IsOnSpecial && CurrentNonSpecialPrice is not null)
            return CurrentNonSpecialPrice;   // still on special: carry the reconstructed "was" forward

        return null;                         // first sighting already on special — unrecoverable
    }
}
