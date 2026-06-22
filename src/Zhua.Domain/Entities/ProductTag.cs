using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>
/// A promo/marketing tag from a store's product feed (plan D13) — e.g. Woolworths "IsSpecial", "IsGreatPrice"
/// (the "Low Price" badge), "IsClubPrice", or an additional "Clearance"/"Organic" tag. Tags are a dimension
/// shared many-to-many with <see cref="StoreProduct"/>: a product can carry several, and the set is reset every
/// crawl to reflect current state (tags are volatile and do NOT go into price history). Scoped per
/// <see cref="Chain"/> because the vocabulary is chain-specific; cross-store normalisation is future work.
/// </summary>
public class ProductTag
{
    public Guid Id { get; set; }

    public Chain Chain { get; set; }

    /// <summary>Which feed slot this came from (primary badge vs additional tag).</summary>
    public ProductTagSource Source { get; set; }

    /// <summary>The source's machine value, e.g. "IsSpecial" or "Clearance". Stable per chain.</summary>
    public required string Code { get; set; }

    /// <summary>Optional friendlier label for display, when the source gives one.</summary>
    public string? Label { get; set; }

    public ICollection<StoreProduct> Products { get; } = new List<StoreProduct>();
}
