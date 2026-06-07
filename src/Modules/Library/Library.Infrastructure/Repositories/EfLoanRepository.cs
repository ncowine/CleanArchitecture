using Library.Application.Abstractions;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Repositories;

internal sealed class EfLoanRepository : ILoanRepository
{
    private readonly LibraryDbContext _db;

    public EfLoanRepository(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Loan loan, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Loans.AddAsync(loan, cancellationToken);
    }

    public async Task<IReadOnlyList<Loan>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        await _db.Loans
            .AsNoTracking()
            .Where(loan => loan.StudentId == studentId)
            .OrderByDescending(loan => loan.BorrowedOn)
            .ToListAsync(cancellationToken);

    public Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken) =>
        _db.Loans.FirstOrDefaultAsync(loan => loan.Id == loanId, cancellationToken);

    public async Task<decimal> GetFineTotalAsync(Guid studentId, CancellationToken cancellationToken)
    {
        // Summed client-side: SQLite can't aggregate decimal server-side (it stores them as TEXT).
        var fines = await _db.Loans
            .AsNoTracking()
            .Where(loan => loan.StudentId == studentId)
            .Select(loan => loan.FineAmount)
            .ToListAsync(cancellationToken);

        return fines.Sum();
    }
}
