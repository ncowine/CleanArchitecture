using BuildingBlocks.Messaging;
using Library.Application.Abstractions;

namespace Library.Application.Catalog;

/// <summary>A catalog title with live total/available copy counts.</summary>
public static class GetBook
{
    public sealed record Query(Guid BookId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        string Isbn,
        string Title,
        string Author,
        string Category,
        int PublishedYear,
        string? Description,
        int TotalCopies,
        int AvailableCopies);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IBookReadService _reads;

        public Handler(IBookReadService reads)
        {
            _reads = reads;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetAsync(query.BookId, cancellationToken);
    }
}
