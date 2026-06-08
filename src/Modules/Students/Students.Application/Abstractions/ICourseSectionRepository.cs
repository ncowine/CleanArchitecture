using Students.Domain;

namespace Students.Application.Abstractions;

public interface ICourseSectionRepository
{
    Task AddAsync(CourseSection section, CancellationToken cancellationToken);

    /// <summary>Tracked load including the roster — the caller mutates it and the unit of work persists.</summary>
    Task<CourseSection?> GetAsync(Guid sectionId, CancellationToken cancellationToken);
}
