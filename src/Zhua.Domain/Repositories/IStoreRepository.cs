using Zhua.Domain.Entities;
using Zhua.Domain.Enums;

namespace Zhua.Domain.Repositories;

/// <summary>
/// Persistence port for the <see cref="Store"/> aggregate (repository-pattern refactor). Per the rich-domain rule,
/// it returns <b>domain entities + primitive aggregates only</b> — never an Application DTO; the Application service
/// shapes the DTO. Implemented in Infrastructure over EF.
/// </summary>
public interface IStoreRepository
{
    /// <summary>Active stores (optionally one supermarket), ordered by chain then name.</summary>
    Task<IReadOnlyList<Store>> ListActiveAsync(Chain? supermarket, CancellationToken ct = default);

    /// <summary>Priced-listing count per store id (DB-side GROUP BY — avoids loading listings to count).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountPricedProductsAsync(CancellationToken ct = default);

    /// <summary>Most recent successful crawl finish time per store id (freshness).</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastSucceededCrawlAsync(CancellationToken ct = default);
}
