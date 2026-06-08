using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Pagination;
using FluentValidation;

namespace Library.Application.Outbox;

/// <summary>Lists a page of outbox messages that failed past the retry cap and were dead-lettered.</summary>
public static class GetDeadLetter
{
    public sealed record Query(int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<DeadLetterEntry>>;

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);

            // Cap the page size — never let a caller ask for an unbounded page.
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<DeadLetterEntry>>
    {
        private readonly IDeadLetterReader _deadLetters;

        public Handler(IDeadLetterReader deadLetters)
        {
            _deadLetters = deadLetters;
        }

        public Task<PagedResult<DeadLetterEntry>> Handle(Query query, CancellationToken cancellationToken) =>
            _deadLetters.GetAsync(query.Page, query.PageSize, cancellationToken);
    }
}
