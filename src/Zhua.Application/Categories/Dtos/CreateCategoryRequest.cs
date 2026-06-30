namespace Zhua.Application.Categories;

/// <summary>Create a curated category (plan D25 phase 3). Kind = Department | Aisle | Shelf.</summary>
public sealed record CreateCategoryRequest(string Kind, string Name, Guid? ParentId);
