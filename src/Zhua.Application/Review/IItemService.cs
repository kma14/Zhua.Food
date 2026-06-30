using Zhua.Application.Common;

namespace Zhua.Application.Review;

/// <summary>Items — the internal join key. Admin-only: create one (then link a product to it via IProductService).</summary>
public interface IItemService
{
    Task<Result<ItemView>> CreateAsync(CreateItemRequest request);

    /// <summary>
    /// Merge the item <paramref name="id"/> into <paramref name="intoId"/> (rework phase 4): repoint its products +
    /// candidates to the survivor, then leave it as a redirect tombstone. Idempotent (re-merging into the same
    /// survivor is a no-op success). 400 self/cycle, 404 unknown, 409 already merged elsewhere.
    /// </summary>
    Task<Result<ItemMergeView>> MergeAsync(Guid id, Guid intoId);
}
