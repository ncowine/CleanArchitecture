using Library.Application.Abstractions;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Repositories;

internal sealed class EfReservationRepository : IReservationRepository
{
    private readonly LibraryDbContext _db;

    public EfReservationRepository(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        // Staging only — the unit of work (TransactionBehavior) owns SaveChanges and the commit.
        await _db.Reservations.AddAsync(reservation, cancellationToken);
    }

    public Task<Reservation?> GetAsync(Guid reservationId, CancellationToken cancellationToken) =>
        _db.Reservations.FirstOrDefaultAsync(reservation => reservation.Id == reservationId, cancellationToken);

    // Tracked (the allocator mutates it): the head of the queue for the book.
    public Task<Reservation?> GetNextPendingAsync(Guid bookId, CancellationToken cancellationToken) =>
        _db.Reservations
            .Where(reservation => reservation.BookId == bookId && reservation.Status == ReservationStatus.Pending)
            .OrderBy(reservation => reservation.QueuePosition)
            .FirstOrDefaultAsync(cancellationToken);

    // Tracked (the allocator renumbers them): pending reservations in queue order.
    public async Task<IReadOnlyList<Reservation>> GetPendingOrderedAsync(Guid bookId, CancellationToken cancellationToken) =>
        await _db.Reservations
            .Where(reservation => reservation.BookId == bookId && reservation.Status == ReservationStatus.Pending)
            .OrderBy(reservation => reservation.QueuePosition)
            .ToListAsync(cancellationToken);

    public Task<int> CountPendingAsync(Guid bookId, CancellationToken cancellationToken) =>
        _db.Reservations.CountAsync(
            reservation => reservation.BookId == bookId && reservation.Status == ReservationStatus.Pending,
            cancellationToken);

    public Task<bool> HasActiveForStudentAsync(Guid studentId, Guid bookId, CancellationToken cancellationToken) =>
        _db.Reservations.AnyAsync(
            reservation => reservation.StudentId == studentId
                && reservation.BookId == bookId
                && (reservation.Status == ReservationStatus.Pending
                    || reservation.Status == ReservationStatus.ReadyForPickup),
            cancellationToken);
}
