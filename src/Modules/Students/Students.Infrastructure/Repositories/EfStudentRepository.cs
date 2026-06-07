using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Repositories;

internal sealed class EfStudentRepository : IStudentRepository
{
    private readonly StudentsDbContext _db;

    public EfStudentRepository(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Student student, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Students.AddAsync(student, cancellationToken);
    }

    public Task<Student?> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.Students.FirstOrDefaultAsync(student => student.Id == studentId, cancellationToken);

    public async Task<IReadOnlyList<StudentHold>> GetHoldsAsync(Guid studentId, CancellationToken cancellationToken) =>
        await _db.Holds
            .AsNoTracking()
            .Where(hold => hold.StudentId == studentId)
            .OrderByDescending(hold => hold.PlacedOnUtc)
            .ToListAsync(cancellationToken);
}
