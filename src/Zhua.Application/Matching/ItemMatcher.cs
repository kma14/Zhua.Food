using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Matching;
using Zhua.Domain.Repositories;
using Zhua.Domain.Services;

namespace Zhua.Application.Matching;

/// <summary>
/// Offline item matcher (plan D9/D18/D30). An anchor-priority cascade — <b>Foodstuffs &gt; Woolworths &gt;
/// FreshChoice</b>, by source data quality — where a product becomes a NEW item's anchor only if it couldn't attach
/// to one at a higher tier:
/// <list type="bullet">
/// <item>Tier 1 — Foodstuffs (New World + PAK'nSAVE) share a <c>productId</c>, so each SKU becomes one item
/// (100% reliable, auto).</item>
/// <item>Tier 2 — Woolworths/FreshChoice attach to a Foodstuffs item by <c>brand + size</c> (hard filter) then the
/// <see cref="IItemMatchingPolicy"/>'s name-token decision: clear winner auto-links, else review queue. FreshChoice
/// has no <c>RawBrand</c> (D26), so its brand is <b>inferred</b> from the name's leading word(s) against the
/// Foodstuffs brand vocabulary (D29) — a wrong guess just fails the hard filter, so it costs nothing.</item>
/// <item>Tier 3 (D30) — Woolworths products Foodstuffs doesn't carry (brand ∉ Foodstuffs vocab) become
/// <c>woolworths:{sku}</c> items; FreshChoice then attaches to them (brand inferred against the WW-anchor brands,
/// which include private-label brands like "WW"/"Macro").</item>
/// <item>Tier 4 (D30) — FreshChoice listings that attached to nothing become <c>freshchoice:{sku}</c> singletons
/// (one FreshChoice store ⇒ always singletons).</item>
/// </list>
/// The cascade makes "every active product has an <c>ItemId</c>" a near-invariant; the one deliberate exception is a
/// product whose brand IS a Foodstuffs brand but that didn't attach (a Tier-2 miss) — it stays unanchored rather than
/// mint a duplicate that would split the cross-store compare. Orchestration only — data access is
/// <see cref="IMatchingRepository"/>, the scoring rule is the domain policy, commits via <see cref="IUnitOfWork"/>.
/// Idempotent: items upserted by <see cref="Item.MatchKey"/>, human decisions honoured, merge tombstones resolved.
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

        // Snapshot for the run-scoped AutoLinked count below (plan D29 — was a DB-wide cumulative count).
        var alreadyLinkedIds = products.Where(p => p.ItemId is not null).Select(p => p.Id).ToHashSet();

        // Load every item so merge-redirect tombstones (rework phase 4) resolve to their live survivor: a merged-away
        // MatchKey must link its products to the survivor, never recreate the tombstone.
        var allItems = await repo.GetAllItemsAsync(ct);
        var itemsById = allItems.ToDictionary(c => c.Id);
        var canonByKey = allItems.Where(c => c.MatchKey != null).ToDictionary(c => c.MatchKey!);
        foreach (var key in canonByKey.Keys.ToList())
            canonByKey[key] = ResolveLive(canonByKey[key], itemsById);

        // --- Tier 1: Foodstuffs share productId → one item per Sku, link every branch's Product. ---
        var foodstuffs = products.Where(p => p.Store.Chain is Chain.NewWorld or Chain.PaknSave);
        foreach (var grp in foodstuffs.GroupBy(p => p.Sku))
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

        // --- Tier 2: index Foodstuffs items by (brand, size) for Woolworths/FreshChoice matching. ---
        var index = new Dictionary<(string, string), List<(Guid itemId, HashSet<string> tokens)>>();
        // The FOODSTUFFS brand vocabulary — FreshChoice's brand guess (D29) is checked against it, and it's the
        // D30 anchor guard ("is this brand sold at Foodstuffs?"). Must be Foodstuffs-only: building it from all items
        // would, on a re-run, pull in the Woolworths/FreshChoice anchor brands from prior runs and then wrongly
        // guard WW private label out of anchoring.
        var foodstuffsBrands = new HashSet<string>();
        var indexed = new HashSet<Guid>();
        foreach (var c in canonByKey.Values)
        {
            if (c.MergedIntoId is not null) continue;   // defensive — values are resolved survivors
            if (!indexed.Add(c.Id)) continue;           // a survivor can sit under several absorbed keys
            var nb = ProductNormalizer.NormalizeBrand(c.Brand);
            if (nb is not null && c.MatchKey?.StartsWith("foodstuffs:", StringComparison.Ordinal) == true)
                foodstuffsBrands.Add(nb);
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

        foreach (var w in products.Where(p => p.Store.Chain is Chain.Woolworths or Chain.FreshChoice))
        {
            if (w.ItemId is not null) continue; // already linked (e.g. approved)

            // FreshChoice publishes no brand (D26) — infer it from the name's leading word(s) against the
            // vocabulary of brands Tier 1 already knows (D29). Woolworths always has RawBrand, so this is a
            // no-op for it.
            var brand = w.RawBrand ?? InferBrandFromName(w.RawName, foodstuffsBrands);
            var brandInferred = w.RawBrand is null && brand is not null;

            var nb = ProductNormalizer.NormalizeBrand(brand);
            var ns = ProductNormalizer.NormalizeSize(w.RawSize);
            if (nb is null || ns is null) continue;
            if (!index.TryGetValue((nb, ns), out var cands)) continue;

            var wTokens = ProductNormalizer.Tokenize(w.RawName, brand);
            var targets = cands
                .Where(c => !rejected.Contains((w.Id, c.itemId)))
                .Select(c => new MatchTarget(c.itemId, c.tokens))
                .ToList();

            var outcome = policy.Evaluate(wTokens, targets);
            if (outcome.ScoredCount == 0) continue;

            if (outcome.AutoLinkItemId is { } linkId)
                w.Item = itemsById[linkId];
            else
                QueueCandidates(w, outcome, brand, brandInferred); // ambiguous/weak → human review
        }

        // --- Tier 3: Woolworths-anchored items for products Foodstuffs doesn't carry (plan D30). Anchor priority is
        //     Foodstuffs > Woolworths > FreshChoice (by data quality); a product becomes a NEW anchor only if it
        //     couldn't attach above AND its brand is NOT a Foodstuffs brand — else it's a Tier-2 miss that belongs to
        //     a Foodstuffs item, and anchoring it would mint a duplicate that splits the cross-store compare. ---
        var anchored = 0;
        foreach (var w in products.Where(p => p.Store.Chain == Chain.Woolworths))
        {
            if (Linked(w)) continue;
            var nb = ProductNormalizer.NormalizeBrand(w.RawBrand);
            if (nb is null || foodstuffsBrands.Contains(nb)) continue; // no brand, or a Foodstuffs brand → not an anchor

            var anchor = UpsertAnchor("woolworths:" + w.Sku, w);
            anchor.Brand = w.RawBrand;
            w.Item = anchor;
            anchored++;
        }

        // Index every Woolworths-anchored item (existing + just-created) so FreshChoice can attach to it. The FC
        // brand is inferred against the WW-anchor brand vocabulary — which includes WW private-label brands the
        // Foodstuffs vocab lacks ("WW", "Macro") — not the Foodstuffs vocab.
        var wwIndex = new Dictionary<(string, string), List<(Guid itemId, HashSet<string> tokens)>>();
        var wwBrands = new HashSet<string>();
        var wwIndexed = new HashSet<Guid>();
        foreach (var c in canonByKey.Values)
        {
            if (c.MergedIntoId is not null || c.MatchKey?.StartsWith("woolworths:", StringComparison.Ordinal) != true) continue;
            if (!wwIndexed.Add(c.Id)) continue;
            var nb = ProductNormalizer.NormalizeBrand(c.Brand);
            if (nb is null) continue;
            wwBrands.Add(nb);
            var ns = ProductNormalizer.NormalizeSize(c.Size);
            if (ns is null) continue;
            var k = (nb, ns);
            if (!wwIndex.TryGetValue(k, out var list)) wwIndex[k] = list = [];
            list.Add((c.Id, ProductNormalizer.Tokenize(c.Name, c.Brand)));
        }

        // Tier 3b: attach still-unlinked FreshChoice listings to a Woolworths anchor (same brand+size+name policy).
        foreach (var fc in products.Where(p => p.Store.Chain == Chain.FreshChoice))
        {
            if (Linked(fc)) continue;
            var brand = InferBrandFromName(fc.RawName, wwBrands);
            var nb = ProductNormalizer.NormalizeBrand(brand);
            var ns = ProductNormalizer.NormalizeSize(fc.RawSize);
            if (nb is null || ns is null || !wwIndex.TryGetValue((nb, ns), out var cands)) continue;

            var tokens = ProductNormalizer.Tokenize(fc.RawName, brand);
            var targets = cands
                .Where(c => !rejected.Contains((fc.Id, c.itemId)))
                .Select(c => new MatchTarget(c.itemId, c.tokens))
                .ToList();
            var outcome = policy.Evaluate(tokens, targets);
            if (outcome.ScoredCount == 0) continue;

            if (outcome.AutoLinkItemId is { } linkId)
                fc.Item = itemsById[linkId];
            else
                QueueCandidates(fc, outcome, brand, brandInferred: true);
        }

        // --- Tier 4: FreshChoice-anchored singletons for whatever still didn't attach (plan D30). One FreshChoice
        //     store ⇒ always singletons. Guard: a name that looks like a Foodstuffs brand is a suspected Tier-2 miss
        //     (should attach to a Foodstuffs item), so leave it for review rather than mint a duplicate. ---
        foreach (var fc in products.Where(p => p.Store.Chain == Chain.FreshChoice))
        {
            if (Linked(fc)) continue;
            if (InferBrandFromName(fc.RawName, foodstuffsBrands) is not null) continue; // suspected Foodstuffs miss

            var anchor = UpsertAnchor("freshchoice:" + fc.Sku, fc);
            fc.Item = anchor;
            anchored++;
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
        // AutoLinked = genuine cross-store links this run (run-scoped, active-store-only — plan D29). Products that
        // became their OWN singleton anchor (Tier 3/4, D30) gained an ItemId too, but they aren't matches — subtract
        // them so the metric still means "listings attached to an item they didn't create". EF fixup has run (post-
        // save), so p.ItemId is populated for every link made this run.
        var newlyLinked = products.Count(p => p.ItemId is not null && !alreadyLinkedIds.Contains(p.Id));
        var pending = await repo.CountPendingCandidatesAsync(ct);
        return new MatchRunResult(totalCanon, newlyLinked - anchored, pending, alreadyDecided);

        // Follow a merge-redirect chain (rework phase 4) to the live survivor; bounded against cycles.
        static Item ResolveLive(Item item, Dictionary<Guid, Item> byId)
        {
            for (var guard = 0; item.MergedIntoId is { } next && byId.TryGetValue(next, out var survivor) && guard < 32; guard++)
                item = survivor;
            return item;
        }

        // Linked either in the DB (ItemId, loaded state) or by a link made earlier THIS run (Item navigation, before
        // EF fixup populates the FK). Tier 3/4 run after Tier 1/2, so both must be checked (plan D30).
        static bool Linked(Product p) => p.ItemId is not null || p.Item is not null;

        // Upsert a lower-tier anchor item by its stable MatchKey (plan D30, mirrors Tier 1's foodstuffs: upsert).
        // Name/Description are owned — seeded once on creation, never re-minted (D25); Size/UoM refresh each run.
        // Brand is set by the caller (Woolworths carries one; FreshChoice doesn't).
        Item UpsertAnchor(string key, Product seed)
        {
            if (!canonByKey.TryGetValue(key, out var item))
            {
                item = new Item { Id = Guid.NewGuid(), MatchKey = key, Name = seed.RawName, Description = seed.RawName, Category = "Uncategorized" };
                repo.AddItem(item);
                canonByKey[key] = item;
                itemsById[item.Id] = item;
            }
            item.Size = seed.RawSize;
            item.UnitOfMeasure = seed.UnitOfMeasure;
            return item;
        }

        // Queue the shortlist for human review, skipping pairs already in the queue (shared by Tier 2 + Tier 3b).
        void QueueCandidates(Product product, MatchOutcome outcome, string? brand, bool brandInferred)
        {
            foreach (var s in outcome.Proposed)
            {
                if (!known.Add((product.Id, s.ItemId))) continue;
                repo.AddCandidate(new MatchCandidate
                {
                    Product = product,
                    Item = itemsById[s.ItemId],
                    Score = Math.Round(s.Score, 3),
                    Status = MatchStatus.Pending,
                    CreatedAt = now,
                    Reason = (brandInferred ? $"brand '{brand}' inferred from name; " : "")
                        + $"brand+size match; name overlap {s.Score:0.00}"
                        + (outcome.ClearWinner ? "" : $"; ambiguous ({outcome.ScoredCount} candidates)"),
                });
            }
        }

        // Try the leading 3, then 2, then 1 word(s) of the name against the known-brand vocabulary (longest/most-
        // specific first: "Meadow Fresh" must win over just "Meadow"). A trailing lone "&" is never a valid
        // phrase boundary — it's extended past rather than counted as one of the significant words, so a 3-word
        // brand like "Beak & Sons" is tried whole instead of truncating to the meaningless "Beak &". Null if no
        // length is a real Foodstuffs brand — a guess that fails the hard filter costs nothing (plan D29).
        static string? InferBrandFromName(string? name, HashSet<string> foodstuffsBrands)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (var take = 3; take >= 1; take--)
            {
                if (words.Length < take) continue;
                var end = take;
                while (end < words.Length && words[end - 1] == "&") end++;
                if (end > words.Length) continue;

                var phrase = string.Join(' ', words[..end]);
                if (ProductNormalizer.NormalizeBrand(phrase) is { } nb && foodstuffsBrands.Contains(nb)) return phrase;
            }
            return null;
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
