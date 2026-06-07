using Microsoft.EntityFrameworkCore;
using Students.Infrastructure.Persistence;
using Students.Contracts;

namespace Students.Infrastructure.Contracts;

/// <summary>
/// Implements the Students module's published <see cref="IStudentDirectory"/> contract against the
/// Students database. A read-only projection — no tracking, only the columns the summary exposes —
/// so consuming modules can read student reference data without touching the Student aggregate.
/// </summary>
internal sealed class StudentDirectory : IStudentDirectory
{
    private readonly StudentsDbContext _db;

    public StudentDirectory(StudentsDbContext db)
    {
        _db = db;
    }

    public Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.Students
            .AsNoTracking()
            .Where(student => student.Id == studentId)
            .Select(student => new StudentSummary(
                student.Id,
                student.FirstName + " " + student.LastName,
                student.Email,
                student.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
}
