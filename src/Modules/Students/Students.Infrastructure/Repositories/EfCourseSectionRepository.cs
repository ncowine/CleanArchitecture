using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Repositories;

internal sealed class EfCourseSectionRepository : ICourseSectionRepository
{
    private readonly StudentsDbContext _db;

    public EfCourseSectionRepository(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(CourseSection section, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.CourseSections.AddAsync(section, cancellationToken);
    }

    // Schedule and roster are owned, so EF loads them with the section automatically. Tracked — the
    // handler mutates the roster (enroll/drop/promote) and the unit of work persists the change.
    public Task<CourseSection?> GetAsync(Guid sectionId, CancellationToken cancellationToken) =>
        _db.CourseSections.FirstOrDefaultAsync(section => section.Id == sectionId, cancellationToken);
}
