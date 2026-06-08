using BuildingBlocks.Pagination;
using Library.Application.Abstractions;
using Library.Application.Reservations;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Reads;

internal sealed class ReservationReadService : IReservationReadService
{
    private readonly LibraryDbContext _db;

    public ReservationReadService(LibraryDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<GetBookReservations.QueueEntry>> GetBookQueueAsync(
        Guid bookId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Reservations
            .AsNoTracking()
            .Where(r => r.BookId == bookId
                && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup));

        var totalCount = await query.CountAsync(cancellationToken);

        // Null position (ready-for-pickup) sorts first, then the pending queue in order.
        var rows = await query
            .OrderBy(r => r.QueuePosition)
            .ThenBy(r => r.ReservedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new { r.Id, r.StudentId, r.Status, r.QueuePosition, r.ReservedOn, r.ExpiresOn })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new GetBookReservations.QueueEntry(
                r.Id, r.StudentId, r.Status.ToString(), r.QueuePosition, r.ReservedOn, r.ExpiresOn))
            .ToList();

        return new PagedResult<GetBookReservations.QueueEntry>(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<GetStudentReservations.StudentReservation>> GetStudentReservationsAsync(
        Guid studentId, CancellationToken cancellationToken)
    {
        var rows = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.StudentId == studentId
                && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.ReadyForPickup))
            .Select(r => new
            {
                r.Id,
                r.BookId,
                r.Status,
                r.QueuePosition,
                r.ExpiresOn,
                BookTitle = _db.Books.Where(b => b.Id == r.BookId).Select(b => b.Title).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new GetStudentReservations.StudentReservation(
                r.Id, r.BookId, r.BookTitle ?? "(unknown)", r.Status.ToString(), r.QueuePosition, r.ExpiresOn))
            .ToList();
    }
}
