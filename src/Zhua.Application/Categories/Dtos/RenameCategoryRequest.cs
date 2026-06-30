namespace Zhua.Application.Categories;

/// <summary>Rename a category's display name (plan D25 phase 3) — its path/slug stay as the stable key.</summary>
public sealed record RenameCategoryRequest(string Name);
