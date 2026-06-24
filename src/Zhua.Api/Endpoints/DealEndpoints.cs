using Microsoft.EntityFrameworkCore;
using Zhua.Api.Contracts;
using Zhua.Domain.Enums;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Endpoints;

public static class DealEndpoints
{
    public static IEndpointRouteBuilder MapDealEndpoints(this IEndpointRouteBuilder app)
    {
        // Current specials (biggest saving first). Optional ?supermarket=Woolworths|NewWorld|PaknSave.
        app.MapGet("/deals", async (string? supermarket, ZhuaDbContext db, int page = 1, int size = 20) =>
        {
            size = Math.Clamp(size, 1, 100);
            page = Math.Max(page, 1);

            Chain? chainFilter = Enum.TryParse<Chain>(supermarket, ignoreCase: true, out var c) ? c : null;
            if (supermarket is not null && chainFilter is null)
                return Results.BadRequest(new { error = $"unknown supermarket '{supermarket}'" });

            var deals = await db.StoreProducts
                .Where(sp => sp.Store.IsActive
                    && sp.IsOnSpecial
                    && sp.CurrentNonSpecialPrice != null
                    && sp.CurrentPrice != null
                    && (chainFilter == null || sp.Store.Chain == chainFilter))
                .OrderByDescending(sp => sp.CurrentNonSpecialPrice - sp.CurrentPrice)
                .Skip((page - 1) * size).Take(size)
                .Select(sp => new DealItem(
                    sp.RawName, sp.RawBrand, sp.ImageUrl, sp.Store.Name, sp.Store.Chain.ToString(),
                    sp.CurrentPrice, sp.CurrentNonSpecialPrice, sp.CurrentNonSpecialPrice - sp.CurrentPrice,
                    sp.UnitPrice, sp.UnitOfMeasure, sp.PriceUpdatedAt, sp.LastSeenAt))
                .ToListAsync();

            return Results.Ok(deals);
        }).WithTags("Deals");

        return app;
    }
}
