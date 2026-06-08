using BuildingBlocks.Messaging;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;
using Students.Contracts;

namespace Library.Application.Loans;

/// <summary>
/// Lend a specific available copy to a student. Validates the student exists (and is eligible) in the main
/// DB, enforces the borrower's active-loan limit, then flips the copy to on-loan and writes the loan — all
/// in one Library-DB transaction. The due date is set by the circulation policy, not the caller.
/// </summary>
public static class BorrowBook
{
    public sealed record Command(Guid StudentId, Guid CopyId)
        : IRequest<Guid>, ILibraryCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.StudentId).NotEmpty();
            RuleFor(command => command.CopyId).NotEmpty();
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ILoanRepository _loans;
        private readonly IBookCopyRepository _copies;
        private readonly IStudentDirectory _students;

        public Handler(ILoanRepository loans, IBookCopyRepository copies, IStudentDirectory students)
        {
            _loans = loans;
            _copies = copies;
            _students = students;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            // Cross-database integrity check: the student lives in the main DB and no FK enforces this.
            var student = await _students.GetAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");
            if (student.Status is "Withdrawn" or "Graduated")
                throw new DomainException($"A {student.Status} student cannot borrow.");

            var activeLoans = await _loans.CountActiveByStudentAsync(student.Id, cancellationToken);
            if (activeLoans >= CirculationPolicy.BorrowerLoanLimit)
                throw new DomainException(
                    $"The student already holds the maximum of {CirculationPolicy.BorrowerLoanLimit} active loans.");

            var copy = await _copies.GetAsync(command.CopyId, cancellationToken)
                ?? throw new DomainException($"No copy exists with id '{command.CopyId}'.");

            copy.MarkOnLoan(); // throws if the copy is not available

            var borrowedOn = DateOnly.FromDateTime(DateTime.UtcNow);
            var loan = Loan.Borrow(
                studentId: student.Id,
                copyId: copy.Id,
                borrowedOn: borrowedOn,
                dueOn: borrowedOn.AddDays(CirculationPolicy.LoanPeriodDays));

            await _loans.AddAsync(loan, cancellationToken);
            return loan.Id;
        }
    }
}
