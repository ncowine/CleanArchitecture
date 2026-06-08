using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Application.Academics;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Reads;

internal sealed class CourseReadService : ICourseReadService
{
    private readonly StudentsDbContext _db;

    public CourseReadService(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task<GetCourse.Response?> GetAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await _db.Courses
            .AsNoTracking()
            .Where(c => c.Id == courseId)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Title,
                c.Description,
                c.Credits,
                c.DepartmentName,
                PrerequisiteIds = c.Prerequisites.Select(p => p.PrerequisiteCourseId).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (course is null)
        {
            return null;
        }

        // Resolve the prerequisite ids to code + title in a single follow-up query.
        var prerequisites = await _db.Courses
            .AsNoTracking()
            .Where(c => course.PrerequisiteIds.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new GetCourse.PrerequisiteDto(c.Id, c.Code, c.Title))
            .ToListAsync(cancellationToken);

        return new GetCourse.Response(
            course.Id, course.Code, course.Title, course.Description, course.Credits, course.DepartmentName, prerequisites);
    }

    public async Task<PagedResult<SearchCourses.CourseListItem>> SearchAsync(
        int page, int pageSize, string? department, CancellationToken cancellationToken)
    {
        var query = _db.Courses.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(course => course.DepartmentName == department);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(course => course.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(course => new SearchCourses.CourseListItem(
                course.Id, course.Code, course.Title, course.Credits, course.DepartmentName))
            .ToListAsync(cancellationToken);

        return new PagedResult<SearchCourses.CourseListItem>(items, page, pageSize, totalCount);
    }
}
