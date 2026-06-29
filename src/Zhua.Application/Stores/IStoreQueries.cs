using Zhua.Domain.Enums;

namespace Zhua.Application.Stores;

/// <summary>The physical stores we track (active only).</summary>
public interface IStoreQueries
{
    Task<IReadOnlyList<StoreView>> ListAsync(Chain? supermarket);
}
