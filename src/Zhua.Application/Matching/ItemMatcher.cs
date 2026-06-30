using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Matching;
using Zhua.Domain.Repositories;
using Zhua.Domain.Services;

namespace Zhua.Application.Matching;

/// <summary>
/// Offline item matcher (plan D9/D18). Two tiers:
/// <list type="bullet">
/// <item>Tier 1 — Foodstuffs (New World + PAK'nSAVE) share a <c>productId</c>, so each SKU becomes one item
/// (100% reliable, auto).</item>
/// <item>Tier 2 — Woolworths has no shared id/GTIN with Foodstuffs, so a Woolworths product is matched to a
/// Foodstuffs-derived item by <c>brand + size</c> (hard filter) then the <see cref="IItemMatchingPolicy"/>'s
/// name-token decision: a clear winner auto-links, ambiguous/weaker goes to the review queue.</item>
/// </list>
/// Orchestration only — data access is <see cref="IMatchingRepository"/>, the scoring rule is the domain policy, and
/// it commits via <see cref="IUnitOfWork"/>. Idempotent: items upserted by <see cref="Item.MatchKey"/>, human
/// decisions honoured and never re-proposed, merge tombstones resolved to their survivor.
/// </summary>
public sealed class ItemMatcher(
    IMatchingRepository repo,
    IItemMatchingPolicy policy,
    IUnitOfWork uow,
    TimeProvider clock) : IItemMatcher
{
    public async Task<MatchRunResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var products = await repo.GetActiveProductsAsync(ct);

        // Load every item so merge-redirect tombstones (rework phase 4) resolve to their live survivor: a merged-away
        // MatchKey must link its products to the survivor, never recreate the tombstone.
        var allItems = await repo.GetAllItemsAsync(ct);
        var itemsById = allItems.ToDictionary(c => c.Id);
        var canonByKey = allItems.Where(c => c.MatchKey != null).ToDictionary(c => c.MatchKey!);
        foreach (var key in canonByKey.Keys.ToList())
            canonByKey[key] = ResolveLive(canonByKey[key], itemsById);

        // --- Tier 1: Foodstuffs share productId → one item per SourceSku, link every branch's Product. ---
        var foodstuffs = products.Where(p => p.Store.Chain is Chain.NewWorld or Chain.PaknSave);
        foreach (var grp in foodstuffs.GroupBy(p => p.SourceSku))
        {
            var key = "foodstuffs:" + grp.Key;
            var rep = grp.OrderByDescending(p => p.RawName.Length).First(); // longest name = most descriptive

            if (!canonByKey.TryGetValue(key, out var canon))
            {
                // Name + Description are owned/stable (plan D25): seed once from the representative listing, then
                // never re-mint them from store data. Description doubles as the match anchor + grouping label.
                // Assign the id up front: the (brand,size) index + auto-link resolve items by id, so a newly-created
                // item must already have one before save.
                canon = new Item { Id = Guid.NewGuid(), MatchKey = key, Name = rep.RawName, Description = rep.RawName, Category = "Uncategorized" };
                repo.AddItem(canon);
                canonByKey[key] = canon;
                itemsById[canon.Id] = canon;
            }
            canon.Brand = rep.RawBrand;
            canon.Size = rep.RawSize;
            canon.UnitOfMeasure = rep.UnitOfMeasure;
            canon.Category = FinestCategory(rep) ?? canon.Category;

            foreach (var sp in grp)
                sp.Item = canon;
        }

        // --- Tier 2: index Foodstuffs items by (brand, size) for Woolworths matching. ---
        var index = new Dictionary<(string, string), List<(Guid itemId, HashSet<string> tokens)>>();
        var indexed = new HashSet<Guid>();
        foreach (var c in canonByKey.Values)
        {
            if (c.MergedIntoId is not null) continue;   // defensive — values are resolved survivors
            if (!indexed.Add(c.Id)) continue;           // a survivor can sit under several absorbed keys
            var nb = ProductNormalizer.NormalizeBrand(c.Brand);
            var ns = ProductNormalizer.NormalizeSize(c.Size);
            if (nb is null || ns is null) continue;
            var k = (nb, ns);
            if (!index.TryGetValue(k, out var list)) index[k] = list = [];
            list.Add((c.Id, ProductNormalizer.Tokenize(c.Name, c.Brand)));
        }

        // Honour prior human decisions; never re-propose an answered pair.
        var decisions = await repo.GetAllCandidatesAsync(ct);
        var rejected = decisions.Where(m => m.Status == MatchStatus.Rejected)
            .Select(m => (m.ProductId, m.ItemId)).ToHashSet();
        var known = decisions.Select(m => (m.ProductId, m.ItemId)).ToHashSet();
        var alreadyDecided = decisions.Count(m => m.Status != MatchStatus.Pending);

        // Apply approved decisions (set the link explicitly), resolving through any merge redirect.
        var byId = products.ToDictionary(p => p.Id);
        foreach (var m in decisions.Where(m => m.Status == MatchStatus.Approved))
            if (byId.TryGetValue(m.ProductId, out var sp) && itemsById.TryGetValue(m.ItemId, out var it))
                sp.ItemId = ResolveLive(it, itemsById).Id;

        foreach (var w in products.Where(p => p.Store.Chain == Chain.Woolworths))
        {
            if (w.ItemId is not null) continue; // already linked (e.g. approved)

            var nb = ProductNormalizer.NormalizeBrand(w.RawBrand);
            var ns = ProductNormalizer.NormalizeSize(w.RawSize);
            if (nb is null || ns is null) continue;
            if (!index.TryGetValue((nb, ns), out var cands)) continue;

            var wTokens = ProductNormalizer.Tokenize(w.RawName, w.RawBrand);
            var targets = cands
                .Where(c => !rejected.Contains((w.Id, c.itemId)))
                .Select(c => new MatchTarget(c.itemId, c.tokens))
                .ToList();

            var outcome = policy.Evaluate(wTokens, targets);
            if (outcome.ScoredCount == 0) continue;

            if (outcome.AutoLinkItemId is { } linkId)
            {
                w.Item = itemsById[linkId];
            }
            else
            {
                // Ambiguous or weak → propose the top few for human review (skip pairs already in the queue).
                foreach (var s in outcome.Proposed)
                {
                    if (!known.Add((w.Id, s.ItemId))) continue;
                    repo.AddCandidate(new MatchCandidate
                    {
                        Product = w,
                        Item = itemsById[s.ItemId],
                        Score = Math.Round(s.Score, 3),
                        Status = MatchStatus.Pending,
                        CreatedAt = now,
                        Reason = $"brand+size match; name overlap {s.Score:0.00}"
                            + (outcome.ClearWinner ? "" : $"; ambiguous ({outcome.ScoredCount} candidates)"),
                    });
                }
            }
        }

        await uow.SaveChangesAsync(ct);

        // Drop now-resolved pending candidates (the product got linked since).
        var stale = await repo.GetResolvedPendingCandidatesAsync(ct);
        if (stale.Count > 0)
        {
            repo.RemoveCandidates(stale);
            await uow.SaveChangesAsync(ct);
        }

        var totalCanon = await repo.CountActiveItemsAsync(ct);
        var linked = await repo.CountLinkedProductsAsync(ct);
        var pending = await repo.CountPendingCandidatesAsync(ct);
        return new MatchRunResult(totalCanon, linked, pending, alreadyDecided);

        // Follow a merge-redirect chain (rework phase 4) to the live survivor; bounded against cycles.
        static Item ResolveLive(Item item, Dictionary<Guid, Item> byId)
        {
            for (var guard = 0; item.MergedIntoId is { } next && byId.TryGetValue(next, out var survivor) && guard < 32; guard++)
                item = survivor;
            return item;
        }

        static string? FinestCategory(Product p)
        {
            foreach (var kind in (ReadOnlySpan<CategoryKind>)[CategoryKind.Shelf, CategoryKind.Aisle, CategoryKind.Department])
            {
                var c = p.Categories.FirstOrDefault(x => x.Kind == kind);
                if (c is not null) return c.Name;
            }
            return null;
        }
    }
}
