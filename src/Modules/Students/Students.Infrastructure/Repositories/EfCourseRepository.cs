using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Repositories;

internal sealed class EfCourseRepository : ICourseRepository
{
    private readonly StudentsDbContext _db;

    public EfCourseRepository(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Course course, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Courses.AddAsync(course, cancellationToken);
    }

    // Prerequisites are an owned collection, so EF loads them with the course automatically.
    public Task<Course?> GetAsync(Guid courseId, CancellationToken cancellationToken) =>
        _db.Courses.FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);

    public Task<bool> ExistsAsync(Guid courseId, CancellationToken cancellationToken) =>
        _db.Courses.AnyAsync(course => course.Id == courseId, cancellationToken);
}
