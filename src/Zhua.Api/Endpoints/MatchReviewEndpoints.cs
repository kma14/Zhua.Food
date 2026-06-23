using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

/// <summary>
/// Admin review queue for cross-store matches (plan D18). These are the only writes the Api makes — they touch
/// already-ingested data, never crawl or migrate (CLAUDE.md). No auth yet (local/admin only).
/// </summary>
public static class MatchReviewEndpoints
{
    public static IEndpointRouteBuilder MapMatchReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/match-candidates").WithTags("Match review");

        // The pending queue (highest-confidence first).
        group.MapGet("/", async (ZhuaDbContext db, int page = 1, int size = 50) =>
        {
            size = Math.Clamp(size, 1, 200);
            page = Math.Max(page, 1);

            var items = await db.MatchCandidates
                .Where(m => m.Status == MatchStatus.Pending)
                .OrderByDescending(m => m.Score).ThenBy(m => m.StoreProduct.RawName)
                .Skip((page - 1) * size).Take(size)
                .Select(m => new MatchCandidateView(
                    m.Id, m.StoreProduct.RawName, m.StoreProduct.RawBrand, m.StoreProduct.RawSize,
                    m.StoreProduct.Store.Chain.ToString(), m.StoreProduct.CurrentPrice,
                    m.CanonicalProduct.Name, m.Score, m.Reason))
                .ToListAsync();

            return Results.Ok(items);
        });

        // Approve → link the product to the canonical, and clear the product's other pending candidates.
        group.MapPost("/{id:guid}/approve", async (Guid id, ZhuaDbContext db, TimeProvider clock) =>
        {
            var m = await db.MatchCandidates.Include(x => x.StoreProduct)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (m is null) return Results.NotFound();
            if (m.Status != MatchStatus.Pending) return Results.Conflict(new { error = $"already {m.Status}" });

            m.Approve(clock.GetUtcNow());
            m.StoreProduct.CanonicalProductId = m.CanonicalProductId;

            var siblings = await db.MatchCandidates
                .Where(x => x.StoreProductId == m.StoreProductId && x.Id != m.Id && x.Status == MatchStatus.Pending)
                .ToListAsync();
            db.MatchCandidates.RemoveRange(siblings);

            await db.SaveChangesAsync();
            return Results.Ok(new { m.Id, status = m.Status.ToString(), m.StoreProduct.CanonicalProductId });
        });

        // Reject → the matcher won't propose this pair again.
        group.MapPost("/{id:guid}/reject", async (Guid id, ZhuaDbContext db, TimeProvider clock) =>
        {
            var m = await db.MatchCandidates.FirstOrDefaultAsync(x => x.Id == id);
            if (m is null) return Results.NotFound();
            if (m.Status != MatchStatus.Pending) return Results.Conflict(new { error = $"already {m.Status}" });

            m.Reject(clock.GetUtcNow());
            await db.SaveChangesAsync();
            return Results.Ok(new { m.Id, status = m.Status.ToString() });
        });

        return app;
    }
}
