using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;
using Students.Contracts;

namespace Library.Application.Loans;

/// <summary>
/// The dominant multi-database read: the student's identity from the main DB (via the published
/// contract) plus a page of their loans from the Library DB, composed in memory. No transaction, no
/// cross-DB join. The student envelope is returned once; the loans themselves are paged.
/// </summary>
public static class GetStudentLoans
{
    public sealed record Query(Guid StudentId, int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<Response>;

    public sealed record Response(
        Guid StudentId,
        string StudentName,
        string Status,
        PagedResult<LoanSummary> Loans);

    public sealed record LoanSummary(
        Guid Id,
        Guid CopyId,
        string BookTitle,
        DateOnly BorrowedOn,
        DateOnly DueOn,
        DateOnly? ReturnedOn,
        decimal FineAmount,
        int RenewalCount);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);

            // Cap the page size — never let a caller ask for an unbounded page.
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, Response>
    {
        private readonly ILoanRepository _loans;
        private readonly IBookReadService _books;
        private readonly IStudentDirectory _students;

        public Handler(ILoanRepository loans, IBookReadService books, IStudentDirectory students)
        {
            _loans = loans;
            _books = books;
            _students = students;
        }

        public async Task<Response> Handle(Query query, CancellationToken cancellationToken)
        {
            var student = await _students.GetAsync(query.StudentId, cancellationToken)
                ?? throw new DomainException($"No student exists with id '{query.StudentId}'.");

            var loans = await _loans.GetByStudentAsync(query.StudentId, query.Page, query.PageSize, cancellationToken);

            // Loans reference a copy by id; resolve each to its book title in one copy → book lookup.
            var copyIds = loans.Items.Select(loan => loan.CopyId).Distinct().ToList();
            var titles = await _books.GetTitlesByCopyAsync(copyIds, cancellationToken);

            var summaries = loans.Items
                .Select(loan => new LoanSummary(
                    loan.Id,
                    loan.CopyId,
                    titles.TryGetValue(loan.CopyId, out var title) ? title : "(unknown)",
                    loan.BorrowedOn,
                    loan.DueOn,
                    loan.ReturnedOn,
                    loan.FineAmount,
                    loan.RenewalCount))
                .ToList();

            return new Response(
                student.Id,
                student.FullName,
                student.Status,
                new PagedResult<LoanSummary>(summaries, loans.Page, loans.PageSize, loans.TotalCount));
        }
    }
}
