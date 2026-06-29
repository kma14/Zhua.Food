namespace Zhua.Application.Categories;

/// <summary>A node in the shared category tree (D22) — Department → Aisle → Shelf.</summary>
public sealed record CategoryNode(
    Guid Id,
    string Kind,              // Department | Aisle | Shelf
    string Name,
    string Slug,
    string Path,             // full slug path, e.g. "meat-poultry-seafood/beef"
    int ProductCount,        // items directly on this node
    int TotalProductCount,   // including all descendants (useful at Department/Aisle level)
    IReadOnlyList<CategoryNode> Children);

/// <summary>Create a curated category (plan D25 phase 3). Kind = Department | Aisle | Shelf.</summary>
public sealed record CreateCategoryRequest(string Kind, string Name, Guid? ParentId);

/// <summary>Rename a category's display name (plan D25 phase 3) — its path/slug stay as the stable key.</summary>
public sealed record RenameCategoryRequest(string Name);

/// <summary>A category as returned by the admin create/rename actions.</summary>
public sealed record CategorySummary(Guid Id, string Kind, string Name, string Slug, string Path, Guid? ParentId);

/// <summary>How many nodes a soft-delete archived (the node + its subtree).</summary>
public sealed record ArchiveCategoryResult(int Archived);
