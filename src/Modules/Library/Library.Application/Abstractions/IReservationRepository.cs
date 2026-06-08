using Library.Domain;

namespace Library.Application.Abstractions;

public interface IReservationRepository
{
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken);

    /// <summary>Tracked load — the caller mutates the reservation and the unit of work persists it.</summary>
    Task<Reservation?> GetAsync(Guid reservationId, CancellationToken cancellationToken);

    /// <summary>The head of the queue for a book (lowest position, still pending). Tracked.</summary>
    Task<Reservation?> GetNextPendingAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>All pending reservations for a book in queue order. Tracked, for renumbering.</summary>
    Task<IReadOnlyList<Reservation>> GetPendingOrderedAsync(Guid bookId, CancellationToken cancellationToken);

    Task<int> CountPendingAsync(Guid bookId, CancellationToken cancellationToken);

    /// <summary>Whether the student already holds an active (pending/ready) reservation for the book.</summary>
    Task<bool> HasActiveForStudentAsync(Guid studentId, Guid bookId, CancellationToken cancellationToken);
}
