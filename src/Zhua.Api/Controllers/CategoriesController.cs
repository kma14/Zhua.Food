using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Api.Queries;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

/// <summary>
/// The shared canonical category tree (plan D22) — the one curated, owned vocabulary. Reads are public; the
/// curation writes (create / rename / soft-delete, plan D25 phase 3) live on the same resource, guarded by the
/// <c>Admin</c> policy. Writes touch already-ingested data only — never crawl or migrate (CLAUDE.md).
/// </summary>
[ApiController]
[Route("categories")]
public sealed class CategoriesController(ZhuaDbContext db) : ControllerBase
{
    // ---- reads (public) ----

    /// <summary>
    /// Department → Aisle → Shelf, with product counts. Optional ?kind= caps the depth returned (Department = top
    /// level only, Aisle = two levels). Optional ?storeId= (repeatable) restricts the counts to products sold at
    /// the given stores ("what's available at my stores"; ids come from GET /stores). Archived nodes are hidden.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Tree([FromQuery] string? kind, [FromQuery] Guid[]? storeId)
    {
        var hasStoreFilter = storeId is { Length: > 0 };

        var cats = await db.CanonicalCategories
            .Where(c => !c.IsArchived) // soft-deleted nodes are hidden from browse (D25 phase 3)
            .Select(c => new { c.Id, c.Kind, c.Name, c.Slug, c.Path, c.ParentId })
            .ToListAsync();

        var counts = await db.CanonicalProducts
            .Where(p => p.CanonicalCategoryId != null
                && (!hasStoreFilter || p.StoreProducts.Any(sp => sp.CurrentPrice != null && storeId!.Contains(sp.StoreId))))
            .GroupBy(p => p.CanonicalCategoryId!.Value)
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

        var tree = roots.OrderBy(r => r.Name).Select(r => Build(r, 0, maxDepth)).ToList();
        return Ok(tree);
    }

    /// <summary>
    /// Products inside a category node (its whole subtree), each merged across stores and shown at its cheapest
    /// store. ?sort=unitPrice (default) | price; optional ?storeId= (repeatable). Same data as
    /// GET /products?category={id} (shared query). An archived id resolves to 404.
    /// </summary>
    [HttpGet("{id:guid}/products")]
    public async Task<IActionResult> Products(
        Guid id, [FromQuery] Guid[]? storeId, [FromQuery] string sort = "unitPrice", [FromQuery] int page = 1, [FromQuery] int size = 20)
    {
        var items = await CategoryProductQuery.RunAsync(db, id, sort, page, size, storeId);
        return items is null ? NotFound() : Ok(items);
    }

    // ---- curation writes (admin only — plan D25 phase 3) ----

    /// <summary>Create a curated category. Path/slug are derived from the name (+ parent); path must be unique.</summary>
    [HttpPost]
    [Authorize("Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest body)
    {
        if (!Enum.TryParse<CategoryKind>(body.Kind, ignoreCase: true, out var kind))
            return BadRequest(new { error = $"unknown kind '{body.Kind}' (Department | Aisle | Shelf)" });

        var name = body.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "name is required" });

        CanonicalCategory? parent = null;
        if (body.ParentId is { } pid)
        {
            parent = await db.CanonicalCategories.FirstOrDefaultAsync(c => c.Id == pid);
            if (parent is null) return NotFound(new { error = "parent category not found" });
            if (parent.IsArchived) return BadRequest(new { error = "parent category is archived" });
        }

        var slug = Slugify(name);
        var path = parent is null ? slug : $"{parent.Path}/{slug}";
        if (await db.CanonicalCategories.AnyAsync(c => c.Path == path))
            return Conflict(new { error = $"a category already exists at path '{path}'" });

        var cat = new CanonicalCategory { Kind = kind, Name = name, Slug = slug, Path = path, ParentId = parent?.Id };
        db.CanonicalCategories.Add(cat);
        await db.SaveChangesAsync();
        return Ok(Summary(cat));
    }

    /// <summary>Rename a category's display name. Path/slug stay (the stable mapper key), so it's not re-created.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize("Admin")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameCategoryRequest body)
    {
        var name = body.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "name is required" });

        var cat = await db.CanonicalCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (cat is null) return NotFound();

        cat.Name = name; // display only — path/slug are the stable identity the mapper upserts by
        await db.SaveChangesAsync();
        return Ok(Summary(cat));
    }

    /// <summary>Soft-delete: archive the node + its whole subtree. Hidden from browse; survives mapper re-runs.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize("Admin")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var cats = await db.CanonicalCategories.ToListAsync();
        var root = cats.FirstOrDefault(c => c.Id == id);
        if (root is null) return NotFound();

        var childrenByParent = cats.Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var count = 0;
        var stack = new Stack<CanonicalCategory>([root]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsArchived) { n.IsArchived = true; count++; }
            if (childrenByParent.TryGetValue(n.Id, out var ch)) foreach (var c in ch) stack.Push(c);
        }

        await db.SaveChangesAsync();
        return Ok(new { archived = count });
    }

    // ---- helpers ----

    private static CategoryNode Build(MutableNode n, int depth, int maxDepth)
    {
        var children = depth < maxDepth
            ? n.Children.OrderBy(c => c.Name).Select(c => Build(c, depth + 1, maxDepth)).ToList()
            : [];
        // Total always counts the whole subtree, even when children are trimmed from the response.
        var total = n.DirectCount + SubtreeCount(n);
        return new CategoryNode(n.Id, n.Kind, n.Name, n.Slug, n.Path, n.DirectCount, total, children);
    }

    private static int SubtreeCount(MutableNode n) => n.Children.Sum(c => c.DirectCount + SubtreeCount(c));

    private static CategorySummary Summary(CanonicalCategory c) =>
        new(c.Id, c.Kind.ToString(), c.Name, c.Slug, c.Path, c.ParentId);

    /// <summary>Matches <c>CanonicalCategoryMapper.Slugify</c> so a curated node lines up with the Foodstuffs taxonomy.</summary>
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
