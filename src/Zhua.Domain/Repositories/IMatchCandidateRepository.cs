using Zhua.Domain.Entities;

namespace Zhua.Domain.Repositories;

/// <summary>Persistence port for the <see cref="MatchCandidate"/> review queue (the admin cross-store match review).</summary>
public interface IMatchCandidateRepository
{
    /// <summary>The pending queue, highest-confidence first, paged — with Product+Store+Item loaded for projection.</summary>
    Task<IReadOnlyList<MatchCandidate>> GetPendingAsync(int page, int size, CancellationToken ct = default);

    /// <summary>A single candidate (tracked, with its Product loaded) for an approve/reject decision.</summary>
    Task<MatchCandidate?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>The still-pending candidates for a listing (tracked) — removed when the listing is resolved.</summary>
    Task<IReadOnlyList<MatchCandidate>> GetPendingByProductAsync(Guid productId, CancellationToken ct = default);

    /// <summary>An item's candidates (tracked) — for repointing/deduping them to the survivor during a merge.</summary>
    Task<IReadOnlyList<MatchCandidate>> GetByItemAsync(Guid itemId, CancellationToken ct = default);

    void Remove(MatchCandidate candidate);
}
