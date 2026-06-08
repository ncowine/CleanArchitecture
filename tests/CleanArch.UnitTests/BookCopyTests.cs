using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class BookCopyTests
{
    private static readonly DateOnly Acquired = new(2026, 1, 1);

    private static BookCopy Copy() =>
        BookCopy.Create(Guid.NewGuid(), "BC-001", CopyCondition.Good, Acquired);

    [Fact]
    public void Create_starts_available_and_uppercases_barcode()
    {
        var copy = BookCopy.Create(Guid.NewGuid(), " bc-001 ", CopyCondition.New, Acquired);

        Assert.Equal(CopyStatus.Available, copy.Status);
        Assert.True(copy.IsAvailable);
        Assert.Equal("BC-001", copy.Barcode);
    }

    [Fact]
    public void Loan_then_return_round_trips_availability()
    {
        var copy = Copy();

        copy.MarkOnLoan();
        Assert.Equal(CopyStatus.OnLoan, copy.Status);

        copy.MarkReturned();
        Assert.Equal(CopyStatus.Available, copy.Status);
    }

    [Fact]
    public void Loaning_a_copy_that_is_not_available_throws()
    {
        var copy = Copy();
        copy.MarkOnLoan();

        Assert.Throws<DomainException>(() => copy.MarkOnLoan());
    }

    [Fact]
    public void Returning_a_copy_that_is_not_on_loan_throws() =>
        Assert.Throws<DomainException>(() => Copy().MarkReturned());

    [Fact]
    public void Withdrawing_a_copy_on_loan_throws()
    {
        var copy = Copy();
        copy.MarkOnLoan();

        Assert.Throws<DomainException>(() => copy.Withdraw());
    }

    [Fact]
    public void An_available_copy_can_be_withdrawn()
    {
        var copy = Copy();

        copy.Withdraw();

        Assert.Equal(CopyStatus.Withdrawn, copy.Status);
    }

    [Fact]
    public void A_withdrawn_copy_cannot_be_marked_lost()
    {
        var copy = Copy();
        copy.Withdraw();

        Assert.Throws<DomainException>(() => copy.MarkLost());
    }
}
