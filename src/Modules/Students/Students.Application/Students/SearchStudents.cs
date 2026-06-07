using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Students.Application.Abstractions;
using Students.Domain;

namespace Students.Application.Students;

/// <summary>
/// Paged student search. Follows the house pattern for list reads: a POST with paging (and filters) in
/// the body, returning a <see cref="PagedResult{T}"/> — keeping URLs clean. Returns the light summary
/// shape (<see cref="GetStudent.Response"/>), reused here rather than redefined.
/// </summary>
public static class SearchStudents
{
    public sealed record Query(int Page = 1, int PageSize = 20, string? Status = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<GetStudent.Response>>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);

            // Cap the page size — never let a caller ask for an unbounded page.
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);

            RuleFor(query => query.Status)
                .Must(status => status is null || Enum.TryParse<StudentStatus>(status, ignoreCase: true, out _))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<StudentStatus>())}.");
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<GetStudent.Response>>
    {
        private readonly IStudentReadService _reads;

        public Handler(IStudentReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<GetStudent.Response>> Handle(Query query, CancellationToken cancellationToken)
        {
            // Validation has already guaranteed Status (if present) parses.
            StudentStatus? status = query.Status is null
                ? null
                : Enum.Parse<StudentStatus>(query.Status, ignoreCase: true);

            return _reads.SearchAsync(query.Page, query.PageSize, status, cancellationToken);
        }
    }
}
