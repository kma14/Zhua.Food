using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Entities;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

/// <summary>
/// Admin actions on a single store product's canonical link — the review steps the candidate queue can't cover
/// (plan D18): when none of the proposed candidates is right, the reviewer either links the listing to a
/// <i>different</i> existing canonical, or creates a brand-new one. Like the match-candidate writes, these touch
/// already-ingested data only — never crawl or migrate (CLAUDE.md). No auth yet (local/admin only).
/// </summary>
public static class StoreProductAdminEndpoints
{
    public static IEndpointRouteBuilder MapStoreProductAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/store-products").WithTags("Match review");

        // Link this listing to an EXISTING canonical (the reviewer searched and picked the right one).
        group.MapPost("/{id:guid}/link-canonical", async (Guid id, LinkCanonicalRequest body, ZhuaDbContext db) =>
        {
            var sp = await db.StoreProducts.FirstOrDefaultAsync(x => x.Id == id);
            if (sp is null) return Results.NotFound(new { error = "store product not found" });

            if (!await db.CanonicalProducts.AnyAsync(c => c.Id == body.CanonicalProductId))
                return Results.NotFound(new { error = "canonical product not found" });

            sp.CanonicalProductId = body.CanonicalProductId;
            await ClearPendingCandidatesAsync(db, id);          // this listing is resolved now
            await db.SaveChangesAsync();
            return Results.Ok(new { sp.Id, sp.CanonicalProductId });
        });

        // Create a NEW canonical from this listing and link it (it's genuinely a new product). Optional body
        // overrides; otherwise name/brand/size come from the listing and category from its finest store category.
        group.MapPost("/{id:guid}/create-canonical", async (Guid id, CreateCanonicalRequest? body, ZhuaDbContext db) =>
        {
            var sp = await db.StoreProducts.Include(x => x.Categories).FirstOrDefaultAsync(x => x.Id == id);
            if (sp is null) return Results.NotFound(new { error = "store product not found" });

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
            await ClearPendingCandidatesAsync(db, id);
            await db.SaveChangesAsync();
            return Results.Ok(new { canonicalProductId = canonical.Id, sp.Id });
        });

        return app;

        static async Task ClearPendingCandidatesAsync(ZhuaDbContext db, Guid storeProductId)
        {
            var pending = await db.MatchCandidates
                .Where(m => m.StoreProductId == storeProductId && m.Status == MatchStatus.Pending)
                .ToListAsync();
            db.MatchCandidates.RemoveRange(pending);
        }

        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
