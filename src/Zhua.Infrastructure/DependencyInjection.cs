using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zhua.Application.Crawling;
using Zhua.Application.Matching;
using Zhua.Application.Reporting;
using Zhua.Domain.Repositories;
using Zhua.Domain.Services;
using Zhua.Infrastructure.Crawling;
using Zhua.Infrastructure.Persistence;
using Zhua.Infrastructure.Repositories;

namespace Zhua.Infrastructure;

/// <summary>
/// Composition helpers split by pipeline (D19) so the read-only query Api only takes persistence, while the
/// Worker opts into the write-side crawling + matching services. Keeps the two pipelines separate (CLAUDE.md).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Read side: the <see cref="ZhuaDbContext"/> + clock + the read/admin use-case implementations the Api depends
    /// on (D27 — controllers see only these Application interfaces, never the DbContext). Used by the Api (and as
    /// the base for the Worker).
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ZhuaDbContext>(o => o.UseNpgsql(connectionString));
        services.TryAddSingleton(TimeProvider.System);

        // Domain repository ports → EF adapters (Infrastructure/Repositories). Application use cases inject these.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IMatchCandidateRepository, MatchCandidateRepository>();

        services.AddScoped<IProductService, ProductService>();     // Application use case (over IProductRepository)
        services.AddScoped<ICategoryService, CategoryService>();   // Application use case (over ICategoryRepository)
        services.AddScoped<IStoreQueries, StoreQueries>();          // Application use case (projects over IStoreRepository)
        services.AddScoped<IDealQueries, DealQueries>();           // Application use case (over IProductRepository)
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IMatchReview, MatchReview>();
        services.AddScoped<IReportQueries, ReportQueries>();        // Application use case (over IProductRepository)
        services.AddScoped<IHealthQueries, HealthQueries>();        // Infrastructure liveness probe (no domain repo)
        return services;
    }

    /// <summary>Write side: the crawl orchestrator (crawler implementations are registered by the host).</summary>
    public static IServiceCollection AddCrawling(this IServiceCollection services)
    {
        services.AddScoped<ICrawlOrchestrator, CrawlOrchestrator>();
        return services;
    }

    /// <summary>Offline item matching (plan D9/D18) + category mapping (D22) — Application use cases over the
    /// matching repository port + the domain matching policy.</summary>
    public static IServiceCollection AddMatching(this IServiceCollection services)
    {
        services.AddScoped<IMatchingRepository, MatchingRepository>();
        services.AddSingleton<IItemMatchingPolicy, HeuristicItemMatchingPolicy>();
        services.AddScoped<IItemMatcher, ItemMatcher>();
        services.AddScoped<ICategoryMapper, CategoryMapper>();
        return services;
    }
}
