using Library.Domain;

namespace Library.Application.Abstractions;

public interface ILoanRepository
{
    Task AddAsync(Loan loan, CancellationToken cancellationToken);

    Task<IReadOnlyList<Loan>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken);

    /// <summary>Tracked load — the caller mutates it and the unit of work persists the change.</summary>
    Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken);

    /// <summary>Sum of all fines currently recorded (committed) against a student's loans.</summary>
    Task<decimal> GetFineTotalAsync(Guid studentId, CancellationToken cancellationToken);
}
