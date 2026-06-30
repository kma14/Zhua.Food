namespace Zhua.Application.Review;

/// <summary>An item (internal — never a shopper label) as returned by the admin create action.</summary>
public sealed record ItemView(Guid Id, string Name, string? Description, string? Brand, string? Size, string Category);
