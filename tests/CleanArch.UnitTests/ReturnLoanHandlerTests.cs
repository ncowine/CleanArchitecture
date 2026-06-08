using Library.Application.Loans;
using Library.Application.Reservations;
using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class ReturnLoanHandlerTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static ReturnLoan.Handler HandlerFor(
        FakeLoanRepository loans, FakeBookCopyRepository copies, FakeReservationRepository reservations) =>
        new(loans, copies, new ReservationAllocator(reservations));

    private static (BookCopy copy, Loan loan) OnLoan()
    {
        var copy = BookCopy.Create(Guid.NewGuid(), "BC-1", CopyCondition.Good, Today);
        copy.MarkOnLoan();
        var loan = Loan.Borrow(Guid.NewGuid(), copy.Id, Today, new DateOnly(2030, 1, 1));
        return (copy, loan);
    }

    [Fact]
    public async Task Returns_the_copy_and_closes_the_loan()
    {
        var (copy, loan) = OnLoan();
        var loans = new FakeLoanRepository();
        loans.Seed(loan);
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var handler = HandlerFor(loans, copies, new FakeReservationRepository());

        var result = await handler.Handle(new ReturnLoan.Command(copy.Id), default);

        Assert.Equal(loan.Id, result.LoanId);
        Assert.True(loan.IsReturned);
        Assert.Equal(CopyStatus.Available, copy.Status);
        Assert.False(result.HeldForReservation);
    }

    [Fact]
    public async Task Returning_with_a_waiting_reservation_holds_the_copy_for_the_next_in_line()
    {
        var (copy, loan) = OnLoan();
        var loans = new FakeLoanRepository();
        loans.Seed(loan);
        var copies = new FakeBookCopyRepository();
        copies.Seed(copy);
        var reservations = new FakeReservationRepository();
        var waiting = Reservation.Place(copy.BookId, Guid.NewGuid(), 1, Today);
        reservations.Seed(waiting);
        var handler = HandlerFor(loans, copies, reservations);

        var result = await handler.Handle(new ReturnLoan.Command(copy.Id), default);

        Assert.True(result.HeldForReservation);
        Assert.Equal(CopyStatus.Reserved, copy.Status);
        Assert.Equal(ReservationStatus.ReadyForPickup, waiting.Status);
        Assert.Equal(copy.Id, waiting.HeldCopyId);
    }

    [Fact]
    public async Task No_active_loan_for_the_copy_throws()
    {
        var handler = HandlerFor(new FakeLoanRepository(), new FakeBookCopyRepository(), new FakeReservationRepository());

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(new ReturnLoan.Command(Guid.NewGuid()), default));
    }
}
