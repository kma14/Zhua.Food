namespace Zhua.Application.Health;

/// <summary>Liveness probe for the DB (the /health/db endpoint).</summary>
public interface IHealthQueries
{
    Task<bool> CanConnectAsync(CancellationToken ct = default);
}
