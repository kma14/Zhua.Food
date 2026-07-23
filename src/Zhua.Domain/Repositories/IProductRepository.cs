using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Domain.Repositories;

/// <summary>
/// Persistence port for the <see cref="Product"/> aggregate (the real per-store listing). Returns domain entities
/// (filtered at the DB — only matching rows load) + primitives; the Application service groups/projects/links.
/// Implemented over EF.
/// </summary>
public interface IProductRepository
{
    /// <summary>Active, priced, available listings matching the text/category/store filters — with Store + Item
    /// loaded for grouping. Unavailable listings (D28) are excluded: a delisted product isn't a price you can pay.</summary>
    Task<IReadOnlyList<Product>> FindListingsAsync(
        string? q, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>A single listing by id (no includes) — existence + its <see cref="Product.ItemId"/> + raw header.</summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The priced, available listings of a group — the item's members if <paramref name="itemId"/> is set,
    /// else just the single listing (an unmatched group of one). Store + Item loaded. Unavailable listings (D28)
    /// are excluded from the compare (their snapshots still show in history).</summary>
    Task<IReadOnlyList<Product>> FindGroupAsync(Guid? itemId, Guid productId, CancellationToken ct = default);

    /// <summary>The group's listings with each one's price snapshots since <paramref name="since"/> + Store loaded.</summary>
    Task<IReadOnlyList<Product>> FindGroupWithHistoryAsync(
        Guid? itemId, Guid productId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Current specials matching the supermarket/category/store filters (saving first), paged, Store
    /// loaded. Only available listings seen since <paramref name="seenSince"/> (D28 freshness guard — a special
    /// no crawl has confirmed recently must not be sold as current).</summary>
    Task<IReadOnlyList<Product>> FindSpecialsAsync(
        Chain? supermarket, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds,
        DateTimeOffset seenSince, int page, int size, CancellationToken ct = default);

    /// <summary>Total specials matching the same filters (for the paged envelope's <c>total</c>).</summary>
    Task<int> CountSpecialsAsync(
        Chain? supermarket, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<Guid>? storeIds,
        DateTimeOffset seenSince, CancellationToken ct = default);

    /// <summary>A single listing (tracked) for a link/unlink write.</summary>
    Task<Product?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>An item's listings (tracked) — for repointing them to the survivor during a merge.</summary>
    Task<IReadOnlyList<Product>> GetByItemForUpdateAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Whether <paramref name="itemId"/> is a valid manual link target: it exists and isn't merged away.</summary>
    Task<bool> IsLinkableItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Global distinct-store count per item over available, priced listings (ignores any store filter) — so
    /// `ProductGroup.Comparable` reflects the item's true cross-store span even when a <c>?storeId=</c> filter has
    /// narrowed the returned listings. Only the given items; empty input → empty map.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountStoresByItemAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Active-store listings counted by (chain, anchor scheme, has-pending-candidate) — the raw signals the
    /// product-status report classifies (D30.1). <see cref="MatchStatusCount.AnchorScheme"/> is the part of the linked
    /// item's <c>MatchKey</c> before ':' (or null when the listing is unlinked); primitives only, the Application
    /// service maps scheme → status. Counted at the DB (a GROUP BY, not a row load).</summary>
    Task<IReadOnlyList<MatchStatusCount>> CountByMatchStatusAsync(CancellationToken ct = default);
}

/// <summary>One (chain, anchor scheme, has-pending-candidate) bucket + its product count (D30.1). Primitives only —
/// <paramref name="AnchorScheme"/> is the mechanical MatchKey prefix (e.g. "foodstuffs"), null when unlinked; the
/// Application report maps it to a shopper-free status (聚合归属 / 待审 / 悬空).</summary>
public sealed record MatchStatusCount(Chain Chain, string? AnchorScheme, bool HasPendingCandidate, int Count);
