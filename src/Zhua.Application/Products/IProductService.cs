using Zhua.Application.Common;

namespace Zhua.Application.Products;

/// <summary>
/// Products — the store-first grouped collection + the admin item-link write (D27). Reads return a nullable result
/// (null ⇒ 404); the write returns <see cref="Result{T}"/> so the Api maps the outcome to an HTTP status.
/// </summary>
public interface IProductService
{
    /// <returns><c>null</c> only when <paramref name="categoryId"/> is given but unknown/archived (→ 404).</returns>
    Task<IReadOnlyList<ProductGroup>?> ListAsync(
        string? q, Guid? categoryId, IReadOnlyList<Guid>? storeIds, int page, int size);

    /// <summary>The group containing <paramref name="productId"/> (its cross-store listings); <c>null</c> if unknown.</summary>
    Task<ProductGroup?> GetGroupAsync(Guid productId);

    /// <summary>Per-store price history across the product's item group; <c>null</c> if the product is unknown.</summary>
    Task<ProductPriceHistory?> GetPriceHistoryAsync(Guid productId, int? days);

    /// <summary>Admin: set/clear the product's item link (<c>itemId == null</c> ⇒ unlink). Clears pending candidates.</summary>
    Task<Result<ProductLinkView>> LinkAsync(Guid productId, Guid? itemId);
}
