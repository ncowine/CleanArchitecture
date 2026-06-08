using Library.Application.Abstractions;
using Library.Domain;

namespace Library.Application.Reservations;

public sealed class ReservationAllocator : IReservationAllocator
{
    private readonly IReservationRepository _reservations;

    public ReservationAllocator(IReservationRepository reservations)
    {
        _reservations = reservations;
    }

    public async Task<bool> TryHoldForNextAsync(BookCopy copy, CancellationToken cancellationToken)
    {
        var next = await _reservations.GetNextPendingAsync(copy.BookId, cancellationToken);
        if (next is null)
        {
            return false;
        }

        copy.HoldForPickup();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        next.MarkReadyForPickup(copy.Id, today, today.AddDays(CirculationPolicy.PickupWindowDays));

        await RenumberPendingAsync(copy.BookId, cancellationToken);
        return true;
    }

    public async Task RenumberPendingAsync(Guid bookId, CancellationToken cancellationToken)
    {
        var pending = await _reservations.GetPendingOrderedAsync(bookId, cancellationToken);

        for (var index = 0; index < pending.Count; index++)
        {
            pending[index].SetQueuePosition(index + 1);
        }
    }
}
