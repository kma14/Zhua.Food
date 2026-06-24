using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

public static class StoreEndpoints
{
    public static IEndpointRouteBuilder MapStoreEndpoints(this IEndpointRouteBuilder app)
    {
        // The physical stores the app tracks prices for (active only — D16 keeps duplicate Woolworths branches
        // inactive). Optional ?supermarket=Woolworths|NewWorld|PaknSave. Used by the front-end for store pickers,
        // a map, and to label/qualify the store names that appear in product comparisons.
        app.MapGet("/stores", async (string? supermarket, ZhuaDbContext db) =>
        {
            Chain? filter = Enum.TryParse<Chain>(supermarket, ignoreCase: true, out var c) ? c : null;
            if (supermarket is not null && filter is null)
                return Results.BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

            var stores = await db.Stores
                .Where(s => s.IsActive && (filter == null || s.Chain == filter))
                .OrderBy(s => s.Chain).ThenBy(s => s.Name)
                .Select(s => new StoreView(
                    s.Id, s.Chain.ToString(), s.Name, s.Suburb, s.Latitude, s.Longitude,
                    s.StoreProducts.Count(sp => sp.CurrentPrice != null),
                    s.CrawlRuns.Where(r => r.Status == CrawlRunStatus.Succeeded)
                        .Max(r => (DateTimeOffset?)r.FinishedAt)))
                .ToListAsync();

            return Results.Ok(stores);
        }).WithTags("Stores");

        return app;
    }
}
