using BuildingBlocks.Pagination;
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

    public async Task<PagedResult<Loan>> GetByStudentAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Loans
            .AsNoTracking()
            .Where(loan => loan.StudentId == studentId);

        // Count and page in SQL — one COUNT plus one windowed SELECT against the same filter.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(loan => loan.BorrowedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Loan>(items, page, pageSize, totalCount);
    }

    public Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken) =>
        _db.Loans.FirstOrDefaultAsync(loan => loan.Id == loanId, cancellationToken);

    public Task<Loan?> GetActiveByCopyAsync(Guid copyId, CancellationToken cancellationToken) =>
        _db.Loans.FirstOrDefaultAsync(
            loan => loan.CopyId == copyId && loan.ReturnedOn == null, cancellationToken);

    public Task<int> CountActiveByStudentAsync(Guid studentId, CancellationToken cancellationToken) =>
        _db.Loans
            .AsNoTracking()
            .CountAsync(loan => loan.StudentId == studentId && loan.ReturnedOn == null, cancellationToken);

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
