using BuildingBlocks.Pagination;
using Library.Application.Catalog;

namespace Library.Application.Abstractions;

/// <summary>Read-side projections for the catalog: a book with live copy counts, search, and copy lists.</summary>
public interface IBookReadService
{
    Task<GetBook.Response?> GetAsync(Guid bookId, CancellationToken cancellationToken);

    Task<PagedResult<SearchBooks.BookListItem>> SearchAsync(
        int page, int pageSize, string? category, string? search, CancellationToken cancellationToken);

    Task<PagedResult<GetBookCopies.CopyListItem>> GetCopiesAsync(
        Guid bookId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Resolves copy ids to their book titles (copy → book join) — used to label loans.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTitlesByCopyAsync(
        IReadOnlyCollection<Guid> copyIds, CancellationToken cancellationToken);
}
