using Zhua.Application.Common;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Domain.Repositories;

namespace Zhua.Application.Categories;

/// <summary>
/// The shared category tree (read) + curation use cases (D22 / D25 phase 3). All logic — tree build, depth cap,
/// subtree counts, CRUD validation, cascade archive — lives here; data access is the <see cref="ICategoryRepository"/>
/// port, and the slug/path identity + archive flip are rich-domain methods on <see cref="Category"/>.
/// </summary>
public sealed class CategoryService(ICategoryRepository categories, IUnitOfWork uow) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryNode>> TreeAsync(string? kind, IReadOnlyList<Guid>? storeIds)
    {
        var cats = await categories.GetActiveAsync();
        var counts = await categories.CountGroupsByCategoryAsync(storeIds);

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
            parent = await categories.GetAsync(pid);
            if (parent is null) return Result<CategorySummary>.NotFound("parent category not found");
            if (parent.IsArchived) return Result<CategorySummary>.BadRequest("parent category is archived");
        }

        var cat = Category.Create(kind, name, parent);   // domain derives slug/path
        if (await categories.PathExistsAsync(cat.Path))
            return Result<CategorySummary>.Conflict($"a category already exists at path '{cat.Path}'");

        categories.Add(cat);
        await uow.SaveChangesAsync();
        return Result<CategorySummary>.Ok(Summary(cat)); // 200 (preserves existing behaviour + api.md)
    }

    public async Task<Result<CategorySummary>> RenameAsync(Guid id, RenameCategoryRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result<CategorySummary>.BadRequest("name is required");

        var cat = await categories.GetAsync(id);
        if (cat is null) return Result<CategorySummary>.NotFound("category not found");

        cat.Rename(name);
        await uow.SaveChangesAsync();
        return Result<CategorySummary>.Ok(Summary(cat));
    }

    public async Task<Result<ArchiveCategoryResult>> ArchiveAsync(Guid id)
    {
        var all = await categories.GetAllAsync();
        var root = all.FirstOrDefault(c => c.Id == id);
        if (root is null) return Result<ArchiveCategoryResult>.NotFound("category not found");

        var childrenByParent = all.Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var count = 0;
        var stack = new Stack<Category>([root]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Archive()) count++;
            if (childrenByParent.TryGetValue(n.Id, out var ch)) foreach (var c in ch) stack.Push(c);
        }

        await uow.SaveChangesAsync();
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

    private sealed record MutableNode(
        Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId, int DirectCount)
    {
        public List<MutableNode> Children { get; } = [];
    }
}
