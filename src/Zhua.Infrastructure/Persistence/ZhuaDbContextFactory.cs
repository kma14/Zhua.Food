using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zhua.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the context without a running app.
/// Reads <c>ConnectionStrings__Default</c>, else a localhost dev default.
/// </summary>
public class ZhuaDbContextFactory : IDesignTimeDbContextFactory<ZhuaDbContext>
{
    public ZhuaDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                   ?? "Host=localhost;Port=5432;Database=zhua;Username=zhua;Password=zhua";

        var options = new DbContextOptionsBuilder<ZhuaDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new ZhuaDbContext(options);
    }
}
