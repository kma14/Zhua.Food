using Microsoft.EntityFrameworkCore;
using Zhua.Domain.Entities;
using Zhua.Domain.Repositories;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>EF implementation of <see cref="IItemRepository"/>.</summary>
public sealed class ItemRepository(ZhuaDbContext db) : IItemRepository
{
    public Task<Item?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);

    public void Add(Item item) => db.Items.Add(item);
}
