using Zhua.Domain.Enums;

namespace Zhua.Application.Deals;

/// <summary>Current specials, biggest dollar saving first.</summary>
public interface IDealQueries
{
    Task<IReadOnlyList<DealItem>> ListAsync(Chain? supermarket, int page, int size);
}
