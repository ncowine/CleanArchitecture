using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;
using Students.Contracts;

namespace Library.Application.Loans;

/// <summary>
/// The dominant multi-database read: the student's identity from the main DB (via the published
/// contract) plus their loans from the Library DB, composed in memory. No transaction, no cross-DB join.
/// </summary>
public static class GetStudentLoans
{
    public sealed record Query(Guid StudentId) : IRequest<Response>;

    public sealed record Response(
        Guid StudentId,
        string StudentName,
        string Status,
        IReadOnlyList<LoanSummary> Loans);

    public sealed record LoanSummary(
        Guid Id,
        string BookTitle,
        DateOnly BorrowedOn,
        DateOnly DueOn,
        DateOnly? ReturnedOn,
        decimal FineAmount);

    public sealed class Handler : IRequestHandler<Query, Response>
    {
        private readonly ILoanRepository _loans;
        private readonly IStudentDirectory _students;

        public Handler(ILoanRepository loans, IStudentDirectory students)
        {
            _loans = loans;
            _students = students;
        }

        public async Task<Response> Handle(Query query, CancellationToken cancellationToken)
        {
            var student = await _students.GetAsync(query.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{query.StudentId}'.");

            var loans = await _loans.GetByStudentAsync(query.StudentId, cancellationToken);

            return new Response(
                student.Id,
                student.FullName,
                student.Status,
                loans
                    .Select(loan => new LoanSummary(
                        loan.Id,
                        loan.BookTitle,
                        loan.BorrowedOn,
                        loan.DueOn,
                        loan.ReturnedOn,
                        loan.FineAmount))
                    .ToList());
        }
    }
}
