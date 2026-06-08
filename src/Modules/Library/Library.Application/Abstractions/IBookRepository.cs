using Library.Domain;

namespace Library.Application.Abstractions;

public interface IBookRepository
{
    Task AddAsync(Book book, CancellationToken cancellationToken);

    Task<Book?> GetAsync(Guid bookId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid bookId, CancellationToken cancellationToken);
}
