using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

/// <summary>
/// Curation of the owned canonical category vocabulary (plan D25 phase 3) — create, rename, soft-delete. The
/// category tree is the one surface we curate (products are an internal join, not curated). Admin writes only:
/// touch already-ingested data, never crawl or migrate (CLAUDE.md). No auth yet (local/admin only).
/// </summary>
[ApiController]
[Route("admin/categories")]
public sealed class CategoriesAdminController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>Create a curated category. Path/slug are derived from the name (+ parent); path must be unique.</summary>
    [HttpPost]
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
}
