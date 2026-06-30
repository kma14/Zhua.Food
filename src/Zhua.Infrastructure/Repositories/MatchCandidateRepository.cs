using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="IMatchCandidateRepository"/>.</summary>
public sealed class MatchCandidateRepository(ZhuaDbContext db) : IMatchCandidateRepository
{
    public async Task<IReadOnlyList<MatchCandidate>> GetPendingAsync(int page, int size, CancellationToken ct = default) =>
        await db.MatchCandidates
            .Where(m => m.Status == MatchStatus.Pending)
            .OrderByDescending(m => m.Score).ThenBy(m => m.Product.RawName)
            .Skip((page - 1) * size).Take(size)
            .Include(m => m.Product).ThenInclude(p => p.Store)
            .Include(m => m.Item)
            .ToListAsync(ct);

    public Task<MatchCandidate?> GetForUpdateAsync(Guid id, CancellationToken ct = default) =>
        db.MatchCandidates.Include(m => m.Product).FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<MatchCandidate>> GetPendingByProductAsync(Guid productId, CancellationToken ct = default) =>
        await db.MatchCandidates
            .Where(m => m.ProductId == productId && m.Status == MatchStatus.Pending)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MatchCandidate>> GetByItemAsync(Guid itemId, CancellationToken ct = default) =>
        await db.MatchCandidates.Where(m => m.ItemId == itemId).ToListAsync(ct);

    public void Remove(MatchCandidate candidate) => db.MatchCandidates.Remove(candidate);
}
