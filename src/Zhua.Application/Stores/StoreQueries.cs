using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Stores;

/// <summary>
/// The active-stores read use case (D27): loads stores + their stats via <see cref="IStoreRepository"/> (Domain
/// port) and shapes the <see cref="StoreView"/> DTO. No EF here — the projection is application logic.
/// </summary>
public sealed class StoreQueries(IStoreRepository stores) : IStoreQueries
{
    public async Task<IReadOnlyList<StoreView>> ListAsync(Chain? supermarket)
    {
        var active = await stores.ListActiveAsync(supermarket);
        var counts = await stores.CountPricedProductsAsync();
        var lastCrawls = await stores.LastSucceededCrawlAsync();

        return active.Select(s => new StoreView(
                s.Id, s.Chain.ToString(), s.Name, s.Suburb, s.Latitude, s.Longitude,
                counts.GetValueOrDefault(s.Id),
                lastCrawls.TryGetValue(s.Id, out var t) ? t : null))
            .ToList();
    }
}
