using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Controllers;

/// <summary>
/// Admin actions on a single store product's canonical link — the review steps the candidate queue can't cover
/// (plan D18): when none of the proposed candidates is right, the reviewer either links the listing to a
/// <i>different</i> existing canonical, or creates a brand-new one. Like the match-candidate writes, these touch
/// already-ingested data only — never crawl or migrate (CLAUDE.md). No auth yet (local/admin only).
/// </summary>
[ApiController]
[Route("admin/store-products")]
public sealed class StoreProductsAdminController(ZhuaDbContext db) : ControllerBase
{
    /// <summary>Link this listing to an EXISTING canonical (the reviewer searched and picked the right one).</summary>
    [HttpPost("{id:guid}/link-canonical")]
    public async Task<IActionResult> LinkCanonical(Guid id, [FromBody] LinkCanonicalRequest body)
    {
        var sp = await db.StoreProducts.FirstOrDefaultAsync(x => x.Id == id);
        if (sp is null) return NotFound(new { error = "store product not found" });

        if (!await db.CanonicalProducts.AnyAsync(c => c.Id == body.CanonicalProductId))
            return NotFound(new { error = "canonical product not found" });

        sp.CanonicalProductId = body.CanonicalProductId;
        await ClearPendingCandidatesAsync(id);          // this listing is resolved now
        await db.SaveChangesAsync();
        return Ok(new { sp.Id, sp.CanonicalProductId });
    }

    /// <summary>
    /// Create a NEW canonical from this listing and link it (it's genuinely a new product). Body is optional;
    /// otherwise name/brand/size come from the listing and category from its finest store category.
    /// </summary>
    [HttpPost("{id:guid}/create-canonical")]
    public async Task<IActionResult> CreateCanonical(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateCanonicalRequest? body)
    {
        var sp = await db.StoreProducts.Include(x => x.Categories).FirstOrDefaultAsync(x => x.Id == id);
        if (sp is null) return NotFound(new { error = "store product not found" });

        // Finest store category (Shelf > Aisle > Department) as the denormalized leaf-name default (plan D9/D22).
        var leaf = sp.Categories.OrderByDescending(c => c.Kind).Select(c => c.Name).FirstOrDefault();

        var canonical = new CanonicalProduct
        {
            Name = Clean(body?.Name) ?? sp.RawName,
            Description = Clean(body?.Name) ?? sp.RawName,   // owned grouping label (plan D25)
            Brand = Clean(body?.Brand) ?? sp.RawBrand,
            Size = Clean(body?.Size) ?? sp.RawSize,
            Category = Clean(body?.Category) ?? leaf ?? "Uncategorised",
            Gtin = sp.Gtin,
        };
        db.CanonicalProducts.Add(canonical);
        sp.CanonicalProduct = canonical;                    // FK is set on save
        await ClearPendingCandidatesAsync(id);
        await db.SaveChangesAsync();
        return Ok(new { canonicalProductId = canonical.Id, sp.Id });
    }

    private async Task ClearPendingCandidatesAsync(Guid storeProductId)
    {
        var pending = await db.MatchCandidates
            .Where(m => m.StoreProductId == storeProductId && m.Status == MatchStatus.Pending)
            .ToListAsync();
        db.MatchCandidates.RemoveRange(pending);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
