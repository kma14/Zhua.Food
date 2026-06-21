using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zhua.Application.Ingestion;
using Zhua.Infrastructure.Ingestion;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence + ingestion services. Crawler implementations are registered by the host.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ZhuaDbContext>(o => o.UseNpgsql(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICrawlOrchestrator, CrawlOrchestrator>();
        return services;
    }
}
