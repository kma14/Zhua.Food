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

    // --- Availability reconciliation (plan D28): a listing that stops appearing in COMPLETE crawls of its store
    // is retired instead of freezing at its last-seen state (the stale-special bug: a 2026-07-13 special served
    // by /deals for a product Highland Park had delisted). Shopper-facing queries exclude unavailable listings.

    /// <summary>False once the listing has been missing from <see cref="MissingRunsBeforeUnavailable"/> consecutive
    /// complete crawls of its store. Reset to true the moment the product is observed again.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>When the current missing streak started (first complete crawl that didn't return the product);
    /// null while the product is being seen.</summary>
    public DateTimeOffset? MissingSince { get; set; }

    /// <summary>Consecutive COMPLETE crawls of this store that did not return the product. Incomplete/failed runs
    /// never touch it — a lost page must not look like a delisting.</summary>
    public int ConsecutiveMissingRuns { get; set; }

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

        // Being observed ends any missing streak (D28) — a listing that returns is available again.
        IsAvailable = true;
        MissingSince = null;
        ConsecutiveMissingRuns = 0;

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

    /// <summary>Missing streaks this long (in complete crawls) mark the listing unavailable (plan D28). At the
    /// twice-daily cadence (D7) that's about one day — one transient source hiccup never retires a product.</summary>
    public const int MissingRunsBeforeUnavailable = 2;

    /// <summary>
    /// Records that a COMPLETE crawl of this store did not return this product (plan D28; the caller — the
    /// orchestrator — only reconciles after a complete run). At <see cref="MissingRunsBeforeUnavailable"/>
    /// consecutive misses the listing is retired: <see cref="IsAvailable"/> goes false and the promo state is
    /// cleared so a stale special can't linger in /deals. <see cref="CurrentPrice"/> is kept as the last-known
    /// price for history/display; deliberately NO synthetic <see cref="PriceSnapshot"/> is written — history
    /// records only real observations (the vanishing shows as <see cref="LastSeenAt"/> going stale).
    /// </summary>
    public void RecordMissing(DateTimeOffset now)
    {
        MissingSince ??= now;
        ConsecutiveMissingRuns++;

        if (ConsecutiveMissingRuns < MissingRunsBeforeUnavailable || !IsAvailable)
            return;

        IsAvailable = false;
        IsOnSpecial = false;
        PromoType = PromoType.None;
        CurrentNonSpecialPrice = null;
        MemberPrice = null;
        MultibuyQuantity = null;
        MultibuyTotal = null;
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
