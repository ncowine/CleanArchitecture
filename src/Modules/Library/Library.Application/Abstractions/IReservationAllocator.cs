using Library.Domain;

namespace Library.Application.Abstractions;

/// <summary>
/// Coordinates a just-freed copy with the hold queue for its book. The single place that decides "a copy
/// is available — does someone in line get it?", reused by return, cancel, and expire.
/// </summary>
public interface IReservationAllocator
{
    /// <summary>
    /// If the copy's book has a pending reservation, holds the copy for the head of the queue (moving that
    /// reservation to ready-for-pickup) and renumbers the rest. Returns true if the copy was held.
    /// </summary>
    Task<bool> TryHoldForNextAsync(BookCopy copy, CancellationToken cancellationToken);

    /// <summary>Renumbers a book's pending reservations to contiguous 1-based positions.</summary>
    Task RenumberPendingAsync(Guid bookId, CancellationToken cancellationToken);
}
