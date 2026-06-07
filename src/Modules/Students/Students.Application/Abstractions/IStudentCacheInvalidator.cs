namespace Students.Application.Abstractions;

/// <summary>
/// Evicts a student's cached reference data. Write handlers that change a student (e.g. withdraw) call
/// this so reads don't serve stale data. EF-free abstraction so the Application layer can depend on it;
/// the implementation lives in Infrastructure over the cache provider.
/// </summary>
public interface IStudentCacheInvalidator
{
    Task RemoveAsync(Guid studentId, CancellationToken cancellationToken);
}
