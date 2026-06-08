using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Reservations;

/// <summary>
/// Expire a held reservation whose pickup window has passed: release the held copy (offering it to the
/// next in line) and mark the reservation expired. A real deployment would run this from a scheduled
/// sweeper; here it's an explicit operation.
/// </summary>
public static class ExpireReservation
{
    public sealed record Command(Guid ReservationId) : IRequest<Guid>, ILibraryCommand, IAuditableRequest;

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IReservationRepository _reservations;
        private readonly IBookCopyRepository _copies;
        private readonly IReservationAllocator _allocator;

        public Handler(
            IReservationRepository reservations, IBookCopyRepository copies, IReservationAllocator allocator)
        {
            _reservations = reservations;
            _copies = copies;
            _allocator = allocator;
        }

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var reservation = await _reservations.GetAsync(command.ReservationId, cancellationToken)
                ?? throw new DomainException($"No reservation exists with id '{command.ReservationId}'.");
            if (reservation.Status != ReservationStatus.ReadyForPickup)
                throw new DomainException("Only a ready-for-pickup reservation can expire.");

            var copy = await _copies.GetAsync(reservation.HeldCopyId!.Value, cancellationToken)
                ?? throw new DomainException("The held copy could not be found.");

            copy.ReleaseHold();
            reservation.Expire();
            await _allocator.TryHoldForNextAsync(copy, cancellationToken);

            return reservation.Id;
        }
    }
}
