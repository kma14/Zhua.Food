using Zhua.Application.Common;
using Zhua.Domain.Entities;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Infrastructure.Services;

/// <summary>
/// EF implementation of <see cref="IItemService"/> (D27). Items are the internal join key; admin creates one from
/// supplied fields, then links a product to it via <see cref="IProductService.LinkAsync"/>.
/// </summary>
public sealed class ItemService(ZhuaDbContext db) : IItemService
{
    public async Task<Result<ItemView>> CreateAsync(CreateItemRequest request)
    {
        var name = Clean(request.Name);
        if (name is null) return Result<ItemView>.BadRequest("name is required");

        var item = new Item
        {
            Name = name,
            Description = Clean(request.Description) ?? name,   // owned grouping label (plan D25)
            Brand = Clean(request.Brand),
            Size = Clean(request.Size),
            Category = Clean(request.Category) ?? "Uncategorised",
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        return Result<ItemView>.Created(
            new ItemView(item.Id, item.Name, item.Description, item.Brand, item.Size, item.Category));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
