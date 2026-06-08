using BuildingBlocks.Pagination;
using Library.Application.Abstractions;
using Library.Application.Catalog;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Reads;

/// <summary>
/// Read projections for the catalog. Copy counts are correlated subqueries over BookCopies; enum→string
/// formatting is done in memory after materialization (EF can't translate enum.ToString()).
/// </summary>
internal sealed class BookReadService : IBookReadService
{
    private readonly LibraryDbContext _db;

    public BookReadService(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task<GetBook.Response?> GetAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var row = await _db.Books
            .AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new
            {
                b.Id,
                b.Isbn,
                b.Title,
                b.Author,
                b.Category,
                b.PublishedYear,
                b.Description,
                TotalCopies = _db.Copies.Count(c => c.BookId == b.Id && c.Status != CopyStatus.Withdrawn),
                AvailableCopies = _db.Copies.Count(c => c.BookId == b.Id && c.Status == CopyStatus.Available),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new GetBook.Response(
            row.Id, row.Isbn, row.Title, row.Author, row.Category.ToString(), row.PublishedYear, row.Description,
            row.TotalCopies, row.AvailableCopies);
    }

    public async Task<PagedResult<SearchBooks.BookListItem>> SearchAsync(
        int page, int pageSize, string? category, string? search, CancellationToken cancellationToken)
    {
        var query = _db.Books.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<BookCategory>(category, ignoreCase: true, out var parsed))
        {
            query = query.Where(b => b.Category == parsed);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => b.Title.Contains(term) || b.Author.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(b => b.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new
            {
                b.Id,
                b.Isbn,
                b.Title,
                b.Author,
                b.Category,
                AvailableCopies = _db.Copies.Count(c => c.BookId == b.Id && c.Status == CopyStatus.Available),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new SearchBooks.BookListItem(
                r.Id, r.Isbn, r.Title, r.Author, r.Category.ToString(), r.AvailableCopies))
            .ToList();

        return new PagedResult<SearchBooks.BookListItem>(items, page, pageSize, totalCount);
    }

    public async Task<PagedResult<GetBookCopies.CopyListItem>> GetCopiesAsync(
        Guid bookId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Copies.AsNoTracking().Where(c => c.BookId == bookId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(c => c.Barcode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.Barcode, c.Condition, c.Status, c.AcquiredOn })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new GetBookCopies.CopyListItem(
                r.Id, r.Barcode, r.Condition.ToString(), r.Status.ToString(), r.AcquiredOn))
            .ToList();

        return new PagedResult<GetBookCopies.CopyListItem>(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetTitlesByCopyAsync(
        IReadOnlyCollection<Guid> copyIds, CancellationToken cancellationToken)
    {
        if (copyIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // copy → book title in one query, keyed by copy id.
        var rows = await _db.Copies
            .AsNoTracking()
            .Where(c => copyIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                Title = _db.Books.Where(b => b.Id == c.BookId).Select(b => b.Title).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.Title ?? "(unknown)");
    }
}
