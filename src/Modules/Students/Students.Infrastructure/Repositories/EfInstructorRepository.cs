using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Repositories;

internal sealed class EfInstructorRepository : IInstructorRepository
{
    private readonly StudentsDbContext _db;

    public EfInstructorRepository(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Instructor instructor, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Instructors.AddAsync(instructor, cancellationToken);
    }

    public Task<bool> ExistsAsync(Guid instructorId, CancellationToken cancellationToken) =>
        _db.Instructors.AnyAsync(instructor => instructor.Id == instructorId, cancellationToken);
}
