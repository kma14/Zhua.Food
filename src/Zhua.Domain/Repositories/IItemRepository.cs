using Zhua.Domain.Entities;

namespace Zhua.Domain.Repositories;

/// <summary>
/// Persistence port for the <see cref="Item"/> aggregate (the internal cross-store join key). Returns domain
/// entities only; the Application service owns create + merge orchestration. Implemented over EF.
/// </summary>
public interface IItemRepository
{
    /// <summary>A single item (tracked) — for merge (source/target + redirect chain).</summary>
    Task<Item?> GetAsync(Guid id, CancellationToken ct = default);

    void Add(Item item);
}
