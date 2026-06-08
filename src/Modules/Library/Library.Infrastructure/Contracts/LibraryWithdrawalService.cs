using Library.Application.Abstractions;
using Library.Contracts;
using Library.Domain;
using Library.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Library.Infrastructure.Contracts;

/// <summary>
/// Implements the Library's reaction to a student withdrawal against the Library DB: returns every active
/// loan (freeing the copy, no overdue fine on withdrawal, offering it to the hold queue) and cancels every
/// active reservation (releasing any held copy to the next in line). Reuses <see cref="IReservationAllocator"/>
/// — it shares this scope's DbContext, so its changes are saved by the single SaveChanges here. Idempotent:
/// a redelivery finds nothing active to clean up.
/// </summary>
internal sealed class LibraryWithdrawalService : ILibraryWithdrawalService
{
    private readonly LibraryDbContext _db;
    private readonly IReservationAllocator _allocator;

    public LibraryWithdrawalService(LibraryDbContext db, IReservationAllocator allocator)
    {
        _db = db;
        _allocator = allocator;
    }

    public async Task OnStudentWithdrawnAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var activeLoans = await _db.Loans
            .Where(loan => loan.StudentId == studentId && loan.ReturnedOn == null)
            .ToListAsync(cancellationToken);

        foreach (var loan in activeLoans)
        {
            loan.Return(today, finePerDayOverdue: 0m); // withdrawal forgives any overdue fine
            var copy = await _db.Copies.FirstOrDefaultAsync(c => c.Id == loan.CopyId, cancellationToken);
            if (copy is { Status: CopyStatus.OnLoan })
            {
                copy.MarkReturned();
                await _allocator.TryHoldForNextAsync(copy, cancellationToken);
            }
        }

        var activeReservations = await _db.Reservations
            .Where(reservation => reservation.StudentId == studentId
                && (reservation.Status == ReservationStatus.Pending
                    || reservation.Status == ReservationStatus.ReadyForPickup))
            .ToListAsync(cancellationToken);

        foreach (var reservation in activeReservations)
        {
            if (reservation.Status == ReservationStatus.ReadyForPickup && reservation.HeldCopyId is { } heldCopyId)
            {
                var copy = await _db.Copies.FirstOrDefaultAsync(c => c.Id == heldCopyId, cancellationToken);
                reservation.Cancel();
                if (copy is { Status: CopyStatus.Reserved })
                {
                    copy.ReleaseHold();
                    await _allocator.TryHoldForNextAsync(copy, cancellationToken);
                }
            }
            else
            {
                var bookId = reservation.BookId;
                reservation.Cancel();
                await _allocator.RenumberPendingAsync(bookId, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
