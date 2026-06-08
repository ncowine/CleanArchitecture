using BuildingBlocks.Pagination;
using Library.Application.Reservations;

namespace Library.Application.Abstractions;

/// <summary>Read-side projections for the hold queue and a student's reservations.</summary>
public interface IReservationReadService
{
    Task<PagedResult<GetBookReservations.QueueEntry>> GetBookQueueAsync(
        Guid bookId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<GetStudentReservations.StudentReservation>> GetStudentReservationsAsync(
        Guid studentId, CancellationToken cancellationToken);
}
