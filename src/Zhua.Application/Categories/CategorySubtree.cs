using Zhua.Domain.Entities;

namespace Zhua.Application.Categories;

/// <summary>
/// Resolves a category node to the id set of its whole active subtree — the shared "filter by category" logic behind
/// both <c>/products?category=</c> and <c>/deals?category=</c>, so the two can't drift. Returns <c>null</c> when the
/// id isn't an active category (unknown/archived → the caller returns 404).
/// </summary>
internal static class CategorySubtree
{
    public static IReadOnlyCollection<Guid>? Resolve(IReadOnlyList<Category> active, Guid rootId)
    {
        if (active.All(c => c.Id != rootId)) return null;

        var childrenByParent = active.Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var set = new HashSet<Guid>();
        var stack = new Stack<Guid>([rootId]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!set.Add(n)) continue;
            if (childrenByParent.TryGetValue(n, out var ch)) foreach (var c in ch) stack.Push(c);
        }
        return set;
    }
}
