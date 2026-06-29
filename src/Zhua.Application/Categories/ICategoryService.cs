using Zhua.Application.Common;

namespace Zhua.Application.Categories;

/// <summary>The shared category tree (reads) + curation (admin writes) — D22/D25 phase 3.</summary>
public interface ICategoryService
{
    Task<IReadOnlyList<CategoryNode>> TreeAsync(string? kind, IReadOnlyList<Guid>? storeIds);
    Task<Result<CategorySummary>> CreateAsync(CreateCategoryRequest request);
    Task<Result<CategorySummary>> RenameAsync(Guid id, RenameCategoryRequest request);
    Task<Result<ArchiveCategoryResult>> ArchiveAsync(Guid id);
}
