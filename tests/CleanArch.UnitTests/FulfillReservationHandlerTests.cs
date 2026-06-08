using Library.Application.Reservations;
using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class FulfillReservationHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static BookCopy HeldCopy()
    {
        var copy = BookCopy.Create(Guid.NewGuid(), "BC-1", CopyCondition.Good, Today);
        copy.MarkOnLoan();
        copy.MarkReturned();
        copy.HoldForPickup(); // Reserved
        return copy;
    }

    [Fact]
    public async Task Fulfilling_a_ready_reservation_issues_a_loan_and_lends_the_copy()
    {
        var copy = HeldCopy();
        var studentId = Guid.NewGuid();
        var reservation = Reservation.Place(copy.BookId, studentId, 1, Today);
        reservation.MarkReadyForPickup(copy.Id, Today, Today.AddDays(3));
        var reservations = new FakeReservationRepository();
        reservations.Seed(reservation);
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var loans = new FakeLoanRepository();
        var handler = new FulfillReservation.Handler(reservations, copies, loans);

        var result = await handler.Handle(new FulfillReservation.Command(reservation.Id), default);

        Assert.Equal(ReservationStatus.Fulfilled, reservation.Status);
        Assert.Equal(CopyStatus.OnLoan, copy.Status);
        var loan = Assert.Single(loans.Added);
        Assert.Equal(result.LoanId, loan.Id);
        Assert.Equal(studentId, loan.StudentId);
    }

    [Fact]
    public async Task Fulfilling_a_reservation_that_is_not_ready_throws()
    {
        var reservation = Reservation.Place(Guid.NewGuid(), Guid.NewGuid(), 1, Today); // pending
        var reservations = new FakeReservationRepository();
        reservations.Seed(reservation);
        var handler = new FulfillReservation.Handler(reservations, new FakeBookCopyRepository(), new FakeLoanRepository());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new FulfillReservation.Command(reservation.Id), default));
    }
}
