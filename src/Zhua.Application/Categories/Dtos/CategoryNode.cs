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
