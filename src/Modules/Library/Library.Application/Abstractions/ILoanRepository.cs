using BuildingBlocks.Pagination;
using Library.Domain;

namespace Library.Application.Abstractions;

public interface ILoanRepository
{
    Task AddAsync(Loan loan, CancellationToken cancellationToken);

    /// <summary>A page of the student's loans, newest first, with the total count for navigation.</summary>
    Task<PagedResult<Loan>> GetByStudentAsync(
        Guid studentId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Tracked load — the caller mutates it and the unit of work persists the change.</summary>
    Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken);

    /// <summary>The active (not-yet-returned) loan for a copy, if any. Tracked, for return/renew.</summary>
    Task<Loan?> GetActiveByCopyAsync(Guid copyId, CancellationToken cancellationToken);

    /// <summary>How many active (not-yet-returned) loans a student currently holds — for the borrow limit.</summary>
    Task<int> CountActiveByStudentAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>Sum of all fines currently recorded (committed) against a student's loans.</summary>
    Task<decimal> GetFineTotalAsync(Guid studentId, CancellationToken cancellationToken);
}
