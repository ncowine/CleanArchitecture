using BuildingBlocks.Pagination;
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

    public async Task AddHoldAsync(StudentHold hold, CancellationToken cancellationToken)
    {
        await _db.Holds.AddAsync(hold, cancellationToken);
    }

    public Task<Student?> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.Students.FirstOrDefaultAsync(student => student.Id == studentId, cancellationToken);

    public async Task<PagedResult<StudentHold>> GetHoldsAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Holds
            .AsNoTracking()
            .Where(hold => hold.StudentId == studentId);

        // Count and page in SQL — one COUNT plus one windowed SELECT against the same filter.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(hold => hold.PlacedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<StudentHold>(items, page, pageSize, totalCount);
    }
}
