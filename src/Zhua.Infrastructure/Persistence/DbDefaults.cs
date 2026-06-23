namespace Zhua.Infrastructure.Persistence;

/// <summary>Shared persistence defaults (D19) — one place for the local-dev connection string.</summary>
public static class DbDefaults
{
    /// <summary>
    /// Local-dev fallback connection string: Docker Postgres on host port 5433 (5432 collides with a native
    /// PostgreSQL on this machine). Real environments override via <c>ConnectionStrings:Default</c> / env vars.
    /// </summary>
    public const string DevConnectionString = "Host=localhost;Port=5433;Database=zhua;Username=zhua;Password=zhua";
}
