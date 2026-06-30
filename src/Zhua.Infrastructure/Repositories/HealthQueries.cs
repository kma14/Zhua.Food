using Microsoft.EntityFrameworkCore;
using Zhua.Application.Health;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Repositories;

/// <summary>
/// EF implementation of <see cref="IHealthQueries"/> — the DB liveness probe. Not a domain repository (health isn't a
/// domain concept); it's an infrastructure-side check, so it lives here implementing the Application port directly.
/// </summary>
public sealed class HealthQueries(ZhuaDbContext db) : IHealthQueries
{
    public Task<bool> CanConnectAsync(CancellationToken ct = default) => db.Database.CanConnectAsync(ct);
}
