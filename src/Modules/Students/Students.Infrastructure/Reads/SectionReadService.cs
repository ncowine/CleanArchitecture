using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Application.Academics;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Reads;

/// <summary>
/// Read projections for sections. Course code/title and instructor names are resolved with correlated
/// subqueries (all three live in the same Students DB). Enum-to-string formatting is done in memory after
/// materialization, never pushed into SQL.
/// </summary>
internal sealed class SectionReadService : ISectionReadService
{
    private readonly StudentsDbContext _db;

    public SectionReadService(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task<GetSection.Response?> GetAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var row = await _db.CourseSections
            .AsNoTracking()
            .Where(s => s.Id == sectionId)
            .Select(s => new
            {
                s.Id,
                s.CourseId,
                s.InstructorId,
                s.Term,
                s.SectionCode,
                s.Capacity,
                s.Status,
                Days = s.Schedule.Days,
                s.Schedule.StartTime,
                s.Schedule.EndTime,
                s.Schedule.Room,
                EnrolledCount = s.Roster.Count(e => e.Status == SectionEnrollmentStatus.Enrolled),
                WaitlistCount = s.Roster.Count(e => e.Status == SectionEnrollmentStatus.Waitlisted),
                CourseCode = _db.Courses.Where(c => c.Id == s.CourseId).Select(c => c.Code).FirstOrDefault(),
                CourseTitle = _db.Courses.Where(c => c.Id == s.CourseId).Select(c => c.Title).FirstOrDefault(),
                InstructorName = _db.Instructors.Where(i => i.Id == s.InstructorId)
                    .Select(i => i.FirstName + " " + i.LastName).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new GetSection.Response(
            row.Id,
            row.CourseId,
            row.CourseCode ?? string.Empty,
            row.CourseTitle ?? string.Empty,
            row.InstructorId,
            row.InstructorName ?? string.Empty,
            row.Term,
            row.SectionCode,
            row.Capacity,
            row.EnrolledCount,
            row.WaitlistCount,
            row.Status.ToString(),
            new GetSection.ScheduleDto(row.Days.ToString(), row.StartTime, row.EndTime, row.Room));
    }

    public async Task<PagedResult<SearchSections.SectionListItem>> SearchAsync(
        int page, int pageSize, string? term, Guid? courseId, Guid? instructorId, CancellationToken cancellationToken)
    {
        var query = _db.CourseSections.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(s => s.Term == term);
        }

        if (courseId is not null)
        {
            query = query.Where(s => s.CourseId == courseId);
        }

        if (instructorId is not null)
        {
            query = query.Where(s => s.InstructorId == instructorId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(s => s.Term)
            .ThenBy(s => s.SectionCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.Term,
                s.SectionCode,
                s.Capacity,
                s.Status,
                EnrolledCount = s.Roster.Count(e => e.Status == SectionEnrollmentStatus.Enrolled),
                CourseCode = _db.Courses.Where(c => c.Id == s.CourseId).Select(c => c.Code).FirstOrDefault(),
                CourseTitle = _db.Courses.Where(c => c.Id == s.CourseId).Select(c => c.Title).FirstOrDefault(),
                InstructorName = _db.Instructors.Where(i => i.Id == s.InstructorId)
                    .Select(i => i.FirstName + " " + i.LastName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new SearchSections.SectionListItem(
                r.Id,
                r.CourseCode ?? string.Empty,
                r.CourseTitle ?? string.Empty,
                r.Term,
                r.SectionCode,
                r.InstructorName ?? string.Empty,
                r.Capacity,
                r.EnrolledCount,
                r.Status.ToString()))
            .ToList();

        return new PagedResult<SearchSections.SectionListItem>(items, page, pageSize, totalCount);
    }

    public async Task<PagedResult<GetSectionRoster.RosterEntry>> GetRosterAsync(
        Guid sectionId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // The roster is an owned collection — reach it through its owning section. Show only the active
        // members (enrolled + waitlisted); dropped/completed entries are history, not the current roster.
        var roster = _db.CourseSections
            .AsNoTracking()
            .Where(s => s.Id == sectionId)
            .SelectMany(s => s.Roster)
            .Where(e => e.Status == SectionEnrollmentStatus.Enrolled || e.Status == SectionEnrollmentStatus.Waitlisted);

        var totalCount = await roster.CountAsync(cancellationToken);

        var rows = await roster
            // Null waitlist position (the enrolled) sorts first, then the waitlist in order.
            .OrderBy(e => e.WaitlistPosition)
            .ThenBy(e => e.EnrolledOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.StudentId,
                e.Status,
                e.WaitlistPosition,
                e.EnrolledOn,
                StudentName = _db.Students.Where(st => st.Id == e.StudentId)
                    .Select(st => st.FirstName + " " + st.LastName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new GetSectionRoster.RosterEntry(
                r.StudentId, r.StudentName ?? string.Empty, r.Status.ToString(), r.WaitlistPosition, r.EnrolledOn))
            .ToList();

        return new PagedResult<GetSectionRoster.RosterEntry>(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<GetStudentSchedule.ScheduledSection>> GetStudentScheduleAsync(
        Guid studentId, string term, CancellationToken cancellationToken)
    {
        var rows = await _db.CourseSections
            .AsNoTracking()
            .Where(s => s.Term == term
                && s.Roster.Any(e => e.StudentId == studentId && e.Status == SectionEnrollmentStatus.Enrolled))
            .Select(s => new
            {
                s.Id,
                s.SectionCode,
                s.Status,
                Days = s.Schedule.Days,
                s.Schedule.StartTime,
                s.Schedule.EndTime,
                s.Schedule.Room,
                CourseCode = _db.Courses.Where(c => c.Id == s.CourseId).Select(c => c.Code).FirstOrDefault(),
                CourseTitle = _db.Courses.Where(c => c.Id == s.CourseId).Select(c => c.Title).FirstOrDefault(),
                InstructorName = _db.Instructors.Where(i => i.Id == s.InstructorId)
                    .Select(i => i.FirstName + " " + i.LastName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new GetStudentSchedule.ScheduledSection(
                r.Id,
                r.CourseCode ?? string.Empty,
                r.CourseTitle ?? string.Empty,
                r.SectionCode,
                r.InstructorName ?? string.Empty,
                r.Status.ToString(),
                r.Days.ToString(),
                r.StartTime,
                r.EndTime,
                r.Room))
            .ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetSatisfiedCourseIdsAsync(Guid studentId, CancellationToken cancellationToken)
    {
        // Satisfied = completed a section of the course with a passing grade (anything but F). Comparing
        // the grade letter (a string column) rather than points avoids SQLite's TEXT-decimal comparison.
        var courseIds = await _db.CourseSections
            .AsNoTracking()
            .Where(s => s.Roster.Any(e => e.StudentId == studentId
                && e.Status == SectionEnrollmentStatus.Completed
                && e.Grade!.Letter != "F"))
            .Select(s => s.CourseId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return courseIds.ToHashSet();
    }
}
