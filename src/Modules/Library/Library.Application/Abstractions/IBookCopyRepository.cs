using Library.Domain;

namespace Library.Application.Abstractions;

public interface IBookCopyRepository
{
    Task AddAsync(BookCopy copy, CancellationToken cancellationToken);

    /// <summary>Tracked load — the caller mutates the copy's status and the unit of work persists it.</summary>
    Task<BookCopy?> GetAsync(Guid copyId, CancellationToken cancellationToken);

    /// <summary>How many copies of a book are currently available to borrow.</summary>
    Task<int> CountAvailableAsync(Guid bookId, CancellationToken cancellationToken);
}
