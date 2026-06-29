using System.Text;
using Microsoft.EntityFrameworkCore;
using Zhua.Application.Common;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>EF implementation of <see cref="ICategoryService"/> (D27) — the shared tree (read) + curation (D25 phase 3).</summary>
public sealed class CategoryService(ZhuaDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryNode>> TreeAsync(string? kind, IReadOnlyList<Guid>? storeIds)
    {
        var hasStoreFilter = storeIds is { Count: > 0 };

        var cats = await db.Categories
            .Where(c => !c.IsArchived) // soft-deleted nodes are hidden from browse (D25 phase 3)
            .Select(c => new { c.Id, c.Kind, c.Name, c.Slug, c.Path, c.ParentId })
            .ToListAsync();

        var counts = await db.Items
            .Where(p => p.CategoryId != null
                && (!hasStoreFilter || p.Products.Any(sp => sp.CurrentPrice != null && storeIds!.Contains(sp.StoreId))))
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        var nodes = cats.ToDictionary(c => c.Id, c => new MutableNode(
            c.Id, c.Kind.ToString(), c.Name, c.Slug, c.Path, c.ParentId, counts.GetValueOrDefault(c.Id)));

        var roots = new List<MutableNode>();
        foreach (var n in nodes.Values)
        {
            if (n.ParentId is { } pid && nodes.TryGetValue(pid, out var parent)) parent.Children.Add(n);
            else roots.Add(n);
        }

        // Optional depth cap by leaf kind to return: Department (0), Aisle (1), Shelf (2 = full, default).
        var maxDepth = kind?.ToLowerInvariant() switch { "department" => 0, "aisle" => 1, _ => 2 };
        return roots.OrderBy(r => r.Name).Select(r => Build(r, 0, maxDepth)).ToList();
    }

    public async Task<Result<CategorySummary>> CreateAsync(CreateCategoryRequest request)
    {
        if (!Enum.TryParse<CategoryKind>(request.Kind, ignoreCase: true, out var kind))
            return Result<CategorySummary>.BadRequest($"unknown kind '{request.Kind}' (Department | Aisle | Shelf)");

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<CategorySummary>.BadRequest("name is required");

        Category? parent = null;
        if (request.ParentId is { } pid)
        {
            parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == pid);
            if (parent is null) return Result<CategorySummary>.NotFound("parent category not found");
            if (parent.IsArchived) return Result<CategorySummary>.BadRequest("parent category is archived");
        }

        var slug = Slugify(name);
        var path = parent is null ? slug : $"{parent.Path}/{slug}";
        if (await db.Categories.AnyAsync(c => c.Path == path))
            return Result<CategorySummary>.Conflict($"a category already exists at path '{path}'");

        var cat = new Category { Kind = kind, Name = name, Slug = slug, Path = path, ParentId = parent?.Id };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        return Result<CategorySummary>.Ok(Summary(cat)); // 200 (preserves existing behaviour + api.md)
    }

    public async Task<Result<CategorySummary>> RenameAsync(Guid id, RenameCategoryRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<CategorySummary>.BadRequest("name is required");

        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return Result<CategorySummary>.NotFound("category not found");

        cat.Name = name; // display only — path/slug are the stable identity the mapper upserts by
        await db.SaveChangesAsync();
        return Result<CategorySummary>.Ok(Summary(cat));
    }

    public async Task<Result<ArchiveCategoryResult>> ArchiveAsync(Guid id)
    {
        var cats = await db.Categories.ToListAsync();
        var root = cats.FirstOrDefault(c => c.Id == id);
        if (root is null) return Result<ArchiveCategoryResult>.NotFound("category not found");

        var childrenByParent = cats.Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var count = 0;
        var stack = new Stack<Category>([root]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsArchived) { n.IsArchived = true; count++; }
            if (childrenByParent.TryGetValue(n.Id, out var ch)) foreach (var c in ch) stack.Push(c);
        }

        await db.SaveChangesAsync();
        return Result<ArchiveCategoryResult>.Ok(new ArchiveCategoryResult(count));
    }

    private static CategoryNode Build(MutableNode n, int depth, int maxDepth)
    {
        var children = depth < maxDepth
            ? n.Children.OrderBy(c => c.Name).Select(c => Build(c, depth + 1, maxDepth)).ToList()
            : [];
        var total = n.DirectCount + SubtreeCount(n); // always counts the whole subtree, even when children are trimmed
        return new CategoryNode(n.Id, n.Kind, n.Name, n.Slug, n.Path, n.DirectCount, total, children);
    }

    private static int SubtreeCount(MutableNode n) => n.Children.Sum(c => c.DirectCount + SubtreeCount(c));

    private static CategorySummary Summary(Category c) =>
        new(c.Id, c.Kind.ToString(), c.Name, c.Slug, c.Path, c.ParentId);

    /// <summary>Matches <c>CategoryMapper.Slugify</c> so a curated node lines up with the Foodstuffs taxonomy.</summary>
    private static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        var prevDash = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); prevDash = false; }
            else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
        }
        return sb.ToString().Trim('-');
    }

    private sealed record MutableNode(
        Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId, int DirectCount)
    {
        public List<MutableNode> Children { get; } = [];
    }
}
