using Microsoft.EntityFrameworkCore;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>EF implementation of <see cref="IHealthQueries"/> (D27) — the DB liveness probe.</summary>
public sealed class HealthQueries(ZhuaDbContext db) : IHealthQueries
{
    public Task<bool> CanConnectAsync(CancellationToken ct = default) => db.Database.CanConnectAsync(ct);
}
