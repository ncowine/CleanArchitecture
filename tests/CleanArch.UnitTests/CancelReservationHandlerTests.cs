using Library.Application.Reservations;
using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class CancelReservationHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    [Fact]
    public async Task Cancelling_a_ready_reservation_hands_the_held_copy_to_the_next_in_line()
    {
        var bookId = Guid.NewGuid();
        var copy = BookCopy.Create(bookId, "BC-1", CopyCondition.Good, Today);
        copy.MarkOnLoan();
        copy.MarkReturned();
        copy.HoldForPickup(); // Reserved, held for the first reservation

        var first = Reservation.Place(bookId, Guid.NewGuid(), 1, Today);
        first.MarkReadyForPickup(copy.Id, Today, Today.AddDays(3));
        var second = Reservation.Place(bookId, Guid.NewGuid(), 1, Today); // next in line

        var reservations = new FakeReservationRepository();
        reservations.Seed(first);
        reservations.Seed(second);
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var handler = new CancelReservation.Handler(reservations, copies, new ReservationAllocator(reservations));

        await handler.Handle(new CancelReservation.Command(first.Id), default);

        Assert.Equal(ReservationStatus.Cancelled, first.Status);
        Assert.Equal(ReservationStatus.ReadyForPickup, second.Status);
        Assert.Equal(copy.Id, second.HeldCopyId);
        Assert.Equal(CopyStatus.Reserved, copy.Status);
    }
}
