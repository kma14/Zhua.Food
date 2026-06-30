namespace Zhua.Application.Categories;

/// <summary>How many nodes a soft-delete archived (the node + its subtree).</summary>
public sealed record ArchiveCategoryResult(int Archived);
