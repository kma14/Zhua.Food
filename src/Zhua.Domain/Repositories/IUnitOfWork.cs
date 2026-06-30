namespace Zhua.Domain.Repositories;

/// <summary>
/// Commits the changes an Application use case made to entities loaded via the repositories (repository-pattern
/// refactor). The service mutates rich domain objects, then calls this once — it never sees EF change-tracking.
/// Implemented in Infrastructure over the DbContext.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
