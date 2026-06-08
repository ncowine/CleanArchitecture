using BuildingBlocks.Messaging;
using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Reservations;

/// <summary>
/// Cancel an active reservation. If a copy was being held for it, the copy is released and offered to the
/// next person in line; otherwise the pending queue is renumbered.
/// </summary>
public static class CancelReservation
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
            if (!reservation.IsActive)
                throw new DomainException("Only an active reservation can be cancelled.");

            if (reservation.Status == ReservationStatus.ReadyForPickup)
            {
                var copy = await _copies.GetAsync(reservation.HeldCopyId!.Value, cancellationToken)
                    ?? throw new DomainException("The held copy could not be found.");

                copy.ReleaseHold();
                reservation.Cancel();
                await _allocator.TryHoldForNextAsync(copy, cancellationToken);
            }
            else
            {
                var bookId = reservation.BookId;
                reservation.Cancel();
                await _allocator.RenumberPendingAsync(bookId, cancellationToken);
            }

            return reservation.Id;
        }
    }
}
