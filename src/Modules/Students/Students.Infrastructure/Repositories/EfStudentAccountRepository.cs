using Microsoft.EntityFrameworkCore;
using Students.Application.Abstractions;
using Students.Domain;
using Students.Infrastructure.Persistence;

namespace Students.Infrastructure.Repositories;

internal sealed class EfStudentAccountRepository : IStudentAccountRepository
{
    private readonly StudentsDbContext _db;

    public EfStudentAccountRepository(StudentsDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(StudentAccount account, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.StudentAccounts.AddAsync(account, cancellationToken);
    }

    // Entries are owned, so EF loads them with the account. Tracked — the handler posts to the ledger.
    public Task<StudentAccount?> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.StudentAccounts.FirstOrDefaultAsync(account => account.StudentId == studentId, cancellationToken);
}
