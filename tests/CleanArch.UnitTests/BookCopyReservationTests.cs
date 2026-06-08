using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class BookCopyReservationTests
{
    private static readonly DateOnly Acquired = new(2026, 1, 1);

    private static BookCopy Available()
    {
        var copy = BookCopy.Create(Guid.NewGuid(), "BC-1", CopyCondition.Good, Acquired);
        copy.MarkOnLoan();
        copy.MarkReturned();
        return copy;
    }

    [Fact]
    public void HoldForPickup_then_lend_round_trips_via_reserved()
    {
        var copy = Available();

        copy.HoldForPickup();
        Assert.Equal(CopyStatus.Reserved, copy.Status);

        copy.LendReserved();
        Assert.Equal(CopyStatus.OnLoan, copy.Status);
    }

    [Fact]
    public void ReleaseHold_returns_a_reserved_copy_to_available()
    {
        var copy = Available();
        copy.HoldForPickup();

        copy.ReleaseHold();

        Assert.Equal(CopyStatus.Available, copy.Status);
    }

    [Fact]
    public void Holding_a_copy_that_is_not_available_throws()
    {
        var copy = BookCopy.Create(Guid.NewGuid(), "BC-1", CopyCondition.Good, Acquired);
        copy.MarkOnLoan();

        Assert.Throws<DomainException>(() => copy.HoldForPickup());
    }

    [Fact]
    public void A_reserved_copy_cannot_be_withdrawn()
    {
        var copy = Available();
        copy.HoldForPickup();

        Assert.Throws<DomainException>(() => copy.Withdraw());
    }
}
