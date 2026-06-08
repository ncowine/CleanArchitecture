using Library.Application.Abstractions;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Repositories;

internal sealed class EfBookCopyRepository : IBookCopyRepository
{
    private readonly LibraryDbContext _db;

    public EfBookCopyRepository(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(BookCopy copy, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Copies.AddAsync(copy, cancellationToken);
    }

    public Task<BookCopy?> GetAsync(Guid copyId, CancellationToken cancellationToken) =>
        _db.Copies.FirstOrDefaultAsync(copy => copy.Id == copyId, cancellationToken);

    public Task<int> CountAvailableAsync(Guid bookId, CancellationToken cancellationToken) =>
        _db.Copies.CountAsync(
            copy => copy.BookId == bookId && copy.Status == CopyStatus.Available, cancellationToken);
}
