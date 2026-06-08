using BuildingBlocks.Messaging;
using Library.Application.Abstractions;

namespace Library.Application.Reservations;

/// <summary>A student's active reservations (with book titles). Inherently small, so returned unpaged.</summary>
public static class GetStudentReservations
{
    public sealed record Query(Guid StudentId) : IRequest<IReadOnlyList<StudentReservation>>;

    public sealed record StudentReservation(
        Guid ReservationId,
        Guid BookId,
        string BookTitle,
        string Status,
        int? QueuePosition,
        DateOnly? ExpiresOn);

    public sealed class Handler : IRequestHandler<Query, IReadOnlyList<StudentReservation>>
    {
        private readonly IReservationReadService _reads;

        public Handler(IReservationReadService reads)
        {
            _reads = reads;
        }

        public Task<IReadOnlyList<StudentReservation>> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetStudentReservationsAsync(query.StudentId, cancellationToken);
    }
}
