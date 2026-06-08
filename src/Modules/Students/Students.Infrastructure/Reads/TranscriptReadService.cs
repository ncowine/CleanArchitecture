using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Application.Academics;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Reads;

/// <summary>
/// Builds a student's academic record from their completed section enrollments. GPA, earned/attempted
/// credits, and standing are computed in memory after materialization — decimal grade points can't be
/// aggregated in SQL on SQLite (it stores decimals as TEXT).
/// </summary>
internal sealed class TranscriptReadService : ITranscriptReadService
{
    private readonly StudentsDbContext _db;

    public TranscriptReadService(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task<GetTranscript.Response> GetTranscriptAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var rows = await FetchCompletedAsync(studentId, cancellationToken);

        var entries = rows
            .OrderBy(r => r.Term)
            .ThenBy(r => r.CourseCode)
            .Select(r => new GetTranscript.TranscriptEntry(
                r.CourseCode, r.CourseTitle, r.Credits, r.Term, r.GradeLetter, r.GradePoints))
            .ToList();

        var (gpa, earned, attempted, standing) = Summarize(rows);

        return new GetTranscript.Response(studentId, entries, gpa, earned, attempted, standing.ToString());
    }

    public async Task<GetAcademicStanding.Response> GetStandingAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var rows = await FetchCompletedAsync(studentId, cancellationToken);
        var (gpa, earned, attempted, standing) = Summarize(rows);

        return new GetAcademicStanding.Response(studentId, gpa, earned, attempted, standing.ToString());
    }

    private async Task<List<CompletedRow>> FetchCompletedAsync(Guid studentId, CancellationToken cancellationToken)
    {
        // Completed enrollments for the student, with the section's course id and term + the grade.
        var enrollments = await _db.CourseSections
            .AsNoTracking()
            .SelectMany(s => s.Roster
                .Where(e => e.StudentId == studentId && e.Status == SectionEnrollmentStatus.Completed)
                .Select(e => new
                {
                    s.CourseId,
                    s.Term,
                    Letter = e.Grade!.Letter,
                    Points = e.Grade!.Points,
                }))
            .ToListAsync(cancellationToken);

        if (enrollments.Count == 0)
        {
            return [];
        }

        // Resolve the courses (code/title/credits) in one follow-up query, then join in memory.
        var courseIds = enrollments.Select(e => e.CourseId).Distinct().ToList();
        var courses = await _db.Courses
            .AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.Title, c.Credits })
            .ToListAsync(cancellationToken);
        var courseById = courses.ToDictionary(c => c.Id);

        return enrollments
            .Select(e =>
            {
                courseById.TryGetValue(e.CourseId, out var course);
                return new CompletedRow(
                    e.Term,
                    course?.Code ?? string.Empty,
                    course?.Title ?? string.Empty,
                    course?.Credits ?? 0,
                    e.Letter,
                    e.Points);
            })
            .ToList();
    }

    private static (decimal Gpa, int Earned, int Attempted, AcademicStanding Standing) Summarize(List<CompletedRow> rows)
    {
        var attempted = rows.Sum(r => r.Credits);
        var earned = rows.Where(r => r.GradeLetter != "F").Sum(r => r.Credits);

        var gpa = attempted > 0
            ? Math.Round(rows.Sum(r => r.GradePoints * r.Credits) / attempted, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return (gpa, earned, attempted, AcademicStandingPolicy.Evaluate(gpa, attempted));
    }

    private sealed record CompletedRow(
        string Term, string CourseCode, string CourseTitle, int Credits, string GradeLetter, decimal GradePoints);
}
