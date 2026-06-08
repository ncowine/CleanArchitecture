using Library.Application.Abstractions;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Repositories;

internal sealed class EfBookRepository : IBookRepository
{
    private readonly LibraryDbContext _db;

    public EfBookRepository(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Book book, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Books.AddAsync(book, cancellationToken);
    }

    public Task<Book?> GetAsync(Guid bookId, CancellationToken cancellationToken) =>
        _db.Books.FirstOrDefaultAsync(book => book.Id == bookId, cancellationToken);

    public Task<bool> ExistsAsync(Guid bookId, CancellationToken cancellationToken) =>
        _db.Books.AnyAsync(book => book.Id == bookId, cancellationToken);
}
