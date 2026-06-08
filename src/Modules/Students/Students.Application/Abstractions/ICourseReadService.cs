using BuildingBlocks.Pagination;
using Students.Application.Academics;

namespace Students.Application.Abstractions;

/// <summary>Read-side projections for course catalog endpoints.</summary>
public interface ICourseReadService
{
    Task<GetCourse.Response?> GetAsync(Guid courseId, CancellationToken cancellationToken);

    Task<PagedResult<SearchCourses.CourseListItem>> SearchAsync(
        int page, int pageSize, string? department, CancellationToken cancellationToken);
}
