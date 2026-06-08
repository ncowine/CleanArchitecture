using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using FluentValidation;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Catalog;

/// <summary>Paged catalog search by category and/or a title/author text match (paging/filters in the body).</summary>
public static class SearchBooks
{
    public sealed record Query(int Page = 1, int PageSize = 20, string? Category = null, string? Search = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<BookListItem>>;

    public sealed record BookListItem(
        Guid Id, string Isbn, string Title, string Author, string Category, int AvailableCopies);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
            RuleFor(query => query.Category)
                .Must(category => category is null || Enum.TryParse<BookCategory>(category, ignoreCase: true, out _))
                .WithMessage($"Category must be one of: {string.Join(", ", Enum.GetNames<BookCategory>())}.");
        }
    }

    public sealed class Handler : IRequestHandler<Query, PagedResult<BookListItem>>
    {
        private readonly IBookReadService _reads;

        public Handler(IBookReadService reads)
        {
            _reads = reads;
        }

        public Task<PagedResult<BookListItem>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.SearchAsync(query.Page, query.PageSize, query.Category, query.Search, cancellationToken);
    }
}
