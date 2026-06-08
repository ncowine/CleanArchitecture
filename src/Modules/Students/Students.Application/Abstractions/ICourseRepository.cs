using Students.Domain;

namespace Students.Application.Abstractions;

public interface ICourseRepository
{
    Task AddAsync(Course course, CancellationToken cancellationToken);

    /// <summary>Tracked load including prerequisites — the caller mutates it and the unit of work persists.</summary>
    Task<Course?> GetAsync(Guid courseId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid courseId, CancellationToken cancellationToken);
}
