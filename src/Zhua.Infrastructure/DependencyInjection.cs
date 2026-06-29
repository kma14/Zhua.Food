using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zhua.Application.Ingestion;
using Zhua.Application.Matching;
using Zhua.Infrastructure.Ingestion;
using Zhua.Infrastructure.Matching;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure;

/// <summary>
/// Composition helpers split by pipeline (D19) so the read-only query Api only takes persistence, while the
/// Worker opts into the write-side ingestion + matching services. Keeps the two pipelines separate (CLAUDE.md).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Read side: the <see cref="ZhuaDbContext"/> + clock. Used by the Api (and as the base for the Worker).</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ZhuaDbContext>(o => o.UseNpgsql(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>Write side: the crawl orchestrator (crawler implementations are registered by the host).</summary>
    public static IServiceCollection AddIngestion(this IServiceCollection services)
    {
        services.AddScoped<ICrawlOrchestrator, CrawlOrchestrator>();
        return services;
    }

    /// <summary>Offline item matching (plan D9/D18) + category mapping (D22).</summary>
    public static IServiceCollection AddMatching(this IServiceCollection services)
    {
        services.AddScoped<IItemMatcher, ItemMatcher>();
        services.AddScoped<ICategoryMapper, CategoryMapper>();
        return services;
    }
}
