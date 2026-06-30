namespace Zhua.Application.Categories;

/// <summary>A category as returned by the admin create/rename actions.</summary>
public sealed record CategorySummary(Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId);
