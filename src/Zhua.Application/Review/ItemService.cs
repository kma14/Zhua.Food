using Zhua.Application.Common;
using Zhua.Domain.Entities;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Review;

/// <summary>
/// Item admin use cases (D27): create an item (internal join key) and merge one into another. Merge spans the Item +
/// Product + MatchCandidate aggregates, so the orchestration lives here over the repository ports; it commits through
/// <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class ItemService(
    IItemRepository items,
    IProductRepository products,
    IMatchCandidateRepository candidates,
    IUnitOfWork uow) : IItemService
{
    public async Task<Result<ItemView>> CreateAsync(CreateItemRequest request)
    {
        var name = Clean(request.Name);
        if (name is null) return Result<ItemView>.BadRequest("name is required");

        var item = new Item
        {
            // A stable manual key (matching.md hardening): makes the hand-made item visible to the matcher's
            // upsert + (brand,size) index, so later Woolworths products can auto-attach and a re-run won't orphan it.
            MatchKey = "manual:" + Guid.NewGuid().ToString("n"),
            Name = name,
            Description = Clean(request.Description) ?? name,   // owned grouping label (plan D25)
            Brand = Clean(request.Brand),
            Size = Clean(request.Size),
            Category = Clean(request.Category) ?? "Uncategorised",
        };
        items.Add(item);
        await uow.SaveChangesAsync();

        return Result<ItemView>.Created(
            new ItemView(item.Id, item.Name, item.Description, item.Brand, item.Size, item.Category));
    }

    public async Task<Result<ItemMergeView>> MergeAsync(Guid id, Guid intoId)
    {
        if (id == intoId) return Result<ItemMergeView>.BadRequest("cannot merge an item into itself");

        var source = await items.GetAsync(id);
        if (source is null) return Result<ItemMergeView>.NotFound("item not found");

        var target = await items.GetAsync(intoId);
        if (target is null) return Result<ItemMergeView>.NotFound("target item not found");

        // Already merged? Idempotent if into the same survivor; otherwise a conflict.
        if (source.MergedIntoId is { } existing)
            return existing == intoId
                ? Result<ItemMergeView>.Ok(new ItemMergeView(id, intoId, 0, 0))
                : Result<ItemMergeView>.Conflict("item is already merged into another item");

        // Follow the target's own redirect chain to the live survivor; reject a cycle back to the source.
        var seen = new HashSet<Guid> { id };
        while (target.MergedIntoId is { } next)
        {
            if (!seen.Add(target.Id) || next == id)
                return Result<ItemMergeView>.BadRequest("merge would create a redirect cycle");
            target = await items.GetAsync(next);
            if (target is null) return Result<ItemMergeView>.NotFound("target item not found");
        }
        if (target.Id == id) return Result<ItemMergeView>.BadRequest("merge would create a redirect cycle");

        var survivorId = target.Id;

        // Repoint products (their price-history snapshots key on ProductId, so history follows the product).
        var moveProducts = await products.GetByItemForUpdateAsync(id);
        foreach (var p in moveProducts) p.ItemId = survivorId;

        // Repoint candidates, dropping any that would duplicate an existing (product, survivor) pair.
        var survivorPairs = (await candidates.GetByItemAsync(survivorId)).Select(m => m.ProductId).ToHashSet();
        var moveCandidates = await candidates.GetByItemAsync(id);
        var moved = 0;
        foreach (var m in moveCandidates)
        {
            if (!survivorPairs.Add(m.ProductId)) { candidates.Remove(m); continue; }
            m.ItemId = survivorId;
            moved++;
        }

        source.MergedIntoId = survivorId;   // redirect tombstone — matcher resolves its key to the survivor
        await uow.SaveChangesAsync();

        return Result<ItemMergeView>.Ok(new ItemMergeView(id, survivorId, moveProducts.Count, moved));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
