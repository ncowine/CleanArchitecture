using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Library.Application.Abstractions;

namespace Library.Application.Catalog;

/// <summary>Paged list of a book's physical copies with their condition and circulation status.</summary>
public static class GetBookCopies
{
    public sealed record Query(Guid BookId, int Page = 1, int PageSize = 20)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<CopyListItem>>;

    public sealed record CopyListItem(Guid Id, string Barcode, string Condition, string Status, DateOnly AcquiredOn);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<CopyListItem>>
    {
        private readonly IBookReadService _reads;

        public Handler(IBookReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<CopyListItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetCopiesAsync(query.BookId, query.Page, query.PageSize, cancellationToken);
    }
}
