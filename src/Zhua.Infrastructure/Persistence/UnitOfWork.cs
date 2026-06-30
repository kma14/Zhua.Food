using Zhua.Domain.Repositories;

namespace Zhua.Infrastructure.Persistence;

/// <summary>EF implementation of <see cref="IUnitOfWork"/> — commits the scoped DbContext's tracked changes.</summary>
public sealed class UnitOfWork(ZhuaDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
