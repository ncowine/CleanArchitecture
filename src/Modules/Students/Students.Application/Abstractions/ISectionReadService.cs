using BuildingBlocks.Pagination;
using Students.Application.Academics;

namespace Students.Application.Abstractions;

/// <summary>Read-side projections for section, roster, and student-schedule endpoints.</summary>
public interface ISectionReadService
{
    Task<GetSection.Response?> GetAsync(Guid sectionId, CancellationToken cancellationToken);

    Task<PagedResult<SearchSections.SectionListItem>> SearchAsync(
        int page, int pageSize, string? term, Guid? courseId, Guid? instructorId, CancellationToken cancellationToken);

    Task<PagedResult<GetSectionRoster.RosterEntry>> GetRosterAsync(
        Guid sectionId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<GetStudentSchedule.ScheduledSection>> GetStudentScheduleAsync(
        Guid studentId, string term, CancellationToken cancellationToken);

    /// <summary>
    /// The set of course ids a student has credit for — used to enforce prerequisites. A course is
    /// satisfied when the student has <b>completed</b> a section of it with a passing grade (not F).
    /// </summary>
    Task<IReadOnlySet<Guid>> GetSatisfiedCourseIdsAsync(Guid studentId, CancellationToken cancellationToken);
}
