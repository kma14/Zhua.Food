using Zhua.Application.Common;

namespace Zhua.Application.Review;

/// <summary>Items — the internal join key. Admin-only: create one (then link a product to it via IProductService).</summary>
public interface IItemService
{
    Task<Result<ItemView>> CreateAsync(CreateItemRequest request);
}
