using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Students.Application.Abstractions;

namespace Students.Application.Billing;

/// <summary>A student's account balance with a paged statement of ledger entries (newest first).</summary>
public static class GetStudentAccount
{
    public sealed record Query(Guid StudentId, int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<Response>;

    public sealed record Response(Guid StudentId, decimal Balance, PagedResult<EntryDto> Entries);

    public sealed record EntryDto(
        Guid Id, string Kind, string? Category, decimal Amount, string Description, DateOnly OccurredOn);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, Response>
    {
        private readonly IAccountReadService _reads;

        public Handler(IAccountReadService reads)
        {
            _reads = reads;
        }

        public Task<Response> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetAsync(query.StudentId, query.Page, query.PageSize, cancellationToken);
    }
}
