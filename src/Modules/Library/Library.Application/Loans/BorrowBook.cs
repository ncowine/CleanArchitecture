using BuildingBlocks.Messaging;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;
using Students.Contracts;

namespace Library.Application.Loans;

/// <summary>
/// Lend a book to a student. Validates the student exists in the main DB before writing the loan.
/// The validator carries only the rule the domain does not (max title length); the rest is enforced
/// by <see cref="Loan.Borrow"/>.
/// </summary>
public static class BorrowBook
{
    public sealed record Command(Guid StudentId, string BookTitle, DateOnly DueOn)
        : IRequest<Guid>, ILibraryCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.BookTitle).NotEmpty().MaximumLength(300);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly ILoanRepository _loans;
        private readonly IStudentDirectory _students;

        public Handler(ILoanRepository loans, IStudentDirectory students)
        {
            _loans = loans;
            _students = students;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            // Cross-database integrity check: the student lives in the main DB and no FK enforces this.
            var student = await _students.GetAsync(command.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");

            var loan = Loan.Borrow(
                studentId: student.Id,
                bookTitle: command.BookTitle,
                borrowedOn: DateOnly.FromDateTime(DateTime.UtcNow),
                dueOn: command.DueOn);

            await _loans.AddAsync(loan, cancellationToken);
            return loan.Id;
        }
    }
}
