using Microsoft.EntityFrameworkCore;
using Zhua.Application.Matching;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Matching;

/// <summary>
/// Offline item matcher (plan D9/D18). Two tiers:
/// <list type="bullet">
/// <item>Tier 1 — Foodstuffs (New World + PAK'nSAVE) share a <c>productId</c>, so each SKU becomes one item
/// (100% reliable, auto).</item>
/// <item>Tier 2 — Woolworths has no shared id/GTIN with Foodstuffs, so we match a Woolworths product to a
/// Foodstuffs-derived item by <c>brand + size</c> (hard filter) then name-token overlap: a single clearly-best
/// candidate auto-links; anything ambiguous/weaker goes to the <see cref="MatchCandidate"/> review queue.</item>
/// </list>
/// Idempotent: items are upserted by <see cref="Item.MatchKey"/>, human decisions are honoured
/// and never re-proposed.
/// </summary>
public sealed class ItemMatcher(ZhuaDbContext db, TimeProvider clock) : IItemMatcher
{
    private const double AutoLinkThreshold = 0.8; // name-token overlap needed to auto-link cross-chain
    private const double CandidateThreshold = 0.3; // below this we don't even propose for review

    public async Task<MatchRunResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var products = await db.Products
            .Include(p => p.Store)
            .Include(p => p.Categories)
            .Where(p => p.Store.IsActive)
            .ToListAsync(ct);

        var canonByKey = await db.Items
            .Where(c => c.MatchKey != null)
            .ToDictionaryAsync(c => c.MatchKey!, ct);

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
                canon = new Item { MatchKey = key, Name = rep.RawName, Description = rep.RawName, Category = "Uncategorized" };
                db.Items.Add(canon);
                canonByKey[key] = canon;
            }
            canon.Brand = rep.RawBrand;
            canon.Size = rep.RawSize;
            canon.UnitOfMeasure = rep.UnitOfMeasure;
            canon.Category = FinestCategory(rep) ?? canon.Category;

            foreach (var sp in grp)
                sp.Item = canon;
        }

        // --- Tier 2: index Foodstuffs items by (brand, size) for Woolworths matching. ---
        var index = new Dictionary<(string, string), List<(Item canon, HashSet<string> tokens)>>();
        foreach (var c in canonByKey.Values)
        {
            var nb = ProductNormalizer.NormalizeBrand(c.Brand);
            var ns = ProductNormalizer.NormalizeSize(c.Size);
            if (nb is null || ns is null) continue;
            var k = (nb, ns);
            if (!index.TryGetValue(k, out var list)) index[k] = list = [];
            list.Add((c, ProductNormalizer.Tokenize(c.Name, c.Brand)));
        }

        // Honour prior human decisions; never re-propose an answered pair.
        var decisions = await db.MatchCandidates.ToListAsync(ct);
        var rejected = decisions.Where(m => m.Status == MatchStatus.Rejected)
            .Select(m => (m.ProductId, m.ItemId)).ToHashSet();
        var known = decisions.Select(m => (m.ProductId, m.ItemId)).ToHashSet();
        var alreadyDecided = decisions.Count(m => m.Status != MatchStatus.Pending);

        // Apply approved decisions (set the link explicitly).
        var byId = products.ToDictionary(p => p.Id);
        foreach (var m in decisions.Where(m => m.Status == MatchStatus.Approved))
            if (byId.TryGetValue(m.ProductId, out var sp))
                sp.ItemId = m.ItemId;

        var autoLinkedWoolworths = 0;
        foreach (var w in products.Where(p => p.Store.Chain == Chain.Woolworths))
        {
            if (w.ItemId is not null) continue; // already linked (e.g. approved)

            var nb = ProductNormalizer.NormalizeBrand(w.RawBrand);
            var ns = ProductNormalizer.NormalizeSize(w.RawSize);
            if (nb is null || ns is null) continue;
            if (!index.TryGetValue((nb, ns), out var cands)) continue;

            var wTokens = ProductNormalizer.Tokenize(w.RawName, w.RawBrand);
            var scored = cands
                .Select(x => (x.canon, score: ProductNormalizer.TokenOverlap(wTokens, x.tokens)))
                .Where(x => x.score >= CandidateThreshold && !rejected.Contains((w.Id, x.canon.Id)))
                .OrderByDescending(x => x.score)
                .ToList();
            if (scored.Count == 0) continue;

            var best = scored[0];
            var clearWinner = scored.Count == 1 || best.score - scored[1].score > 0.001;

            if (best.score >= AutoLinkThreshold && clearWinner)
            {
                w.Item = best.canon;
                autoLinkedWoolworths++;
            }
            else
            {
                // Ambiguous or weak → propose the top few for human review (skip pairs already in the queue).
                foreach (var (canon, score) in scored.Take(3))
                {
                    if (!known.Add((w.Id, canon.Id))) continue;
                    db.MatchCandidates.Add(new MatchCandidate
                    {
                        Product = w,
                        Item = canon,
                        Score = Math.Round(score, 3),
                        Status = MatchStatus.Pending,
                        CreatedAt = now,
                        Reason = $"brand+size match; name overlap {score:0.00}"
                            + (clearWinner ? "" : $"; ambiguous ({scored.Count} candidates)"),
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);

        // Drop now-resolved pending candidates (the product got linked since).
        var stale = await db.MatchCandidates
            .Where(m => m.Status == MatchStatus.Pending && m.Product.ItemId != null)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.MatchCandidates.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }

        var totalCanon = await db.Items.CountAsync(ct);
        var linked = await db.Products.CountAsync(p => p.ItemId != null, ct);
        var pending = await db.MatchCandidates.CountAsync(m => m.Status == MatchStatus.Pending, ct);
        return new MatchRunResult(totalCanon, linked, pending, alreadyDecided);

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
