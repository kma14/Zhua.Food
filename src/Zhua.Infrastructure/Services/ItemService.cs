using Microsoft.EntityFrameworkCore;
using Zhua.Application.Common;
using Zhua.Domain.Entities;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>
/// EF implementation of <see cref="IItemService"/> (D27). Items are the internal join key; admin creates one from
/// supplied fields, then links a product to it via <see cref="IProductService.LinkAsync"/>.
/// </summary>
public sealed class ItemService(ZhuaDbContext db) : IItemService
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
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return Result<ItemView>.Created(
            new ItemView(item.Id, item.Name, item.Description, item.Brand, item.Size, item.Category));
    }

    public async Task<Result<ItemMergeView>> MergeAsync(Guid id, Guid intoId)
    {
        if (id == intoId) return Result<ItemMergeView>.BadRequest("cannot merge an item into itself");

        var source = await db.Items.FirstOrDefaultAsync(i => i.Id == id);
        if (source is null) return Result<ItemMergeView>.NotFound("item not found");

        var target = await db.Items.FirstOrDefaultAsync(i => i.Id == intoId);
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
            target = await db.Items.FirstOrDefaultAsync(i => i.Id == next);
            if (target is null) return Result<ItemMergeView>.NotFound("target item not found");
        }
        if (target.Id == id) return Result<ItemMergeView>.BadRequest("merge would create a redirect cycle");

        var survivorId = target.Id;

        // Repoint products (their price-history snapshots key on ProductId, so history follows the product — no
        // separate handling).
        var products = await db.Products.Where(p => p.ItemId == id).ToListAsync();
        foreach (var p in products) p.ItemId = survivorId;

        // Repoint candidates, dropping any that would duplicate an existing (product, survivor) pair.
        var existingPairs = await db.MatchCandidates
            .Where(m => m.ItemId == survivorId)
            .Select(m => m.ProductId).ToListAsync();
        var survivorPairs = existingPairs.ToHashSet();
        var candidates = await db.MatchCandidates.Where(m => m.ItemId == id).ToListAsync();
        var moved = 0;
        foreach (var m in candidates)
        {
            if (!survivorPairs.Add(m.ProductId)) { db.MatchCandidates.Remove(m); continue; }
            m.ItemId = survivorId;
            moved++;
        }

        source.MergedIntoId = survivorId;   // redirect tombstone — matcher resolves its key to the survivor
        await db.SaveChangesAsync();

        return Result<ItemMergeView>.Ok(new ItemMergeView(id, survivorId, products.Count, moved));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
