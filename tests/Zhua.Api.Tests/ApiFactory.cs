using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

/// <summary>
/// Boots the real <c>Zhua.Api</c> against a throwaway Postgres (Testcontainers) so the integration tests exercise
/// the actual Npgsql query translation, not an in-memory fake. Migrates the schema (D5: the Api never migrates —
/// the test harness does) and seeds <see cref="TestData"/> once for the whole run.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Replace the app's Npgsql DbContext (dev connection string) with one pointed at the throwaway container.
        // Done in services (not config) so it can't be missed by the minimal-hosting config timing.
        builder.ConfigureTestServices(services =>
        {
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ZhuaDbContext>) || d.ServiceType == typeof(ZhuaDbContext))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<ZhuaDbContext>(o => o.UseNpgsql(_db.GetConnectionString()));
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope(); // builds the host (with the container conn string) on first access
        var ctx = scope.ServiceProvider.GetRequiredService<ZhuaDbContext>();
        await ctx.Database.MigrateAsync();
        await TestData.SeedAsync(ctx);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
