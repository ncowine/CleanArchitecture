using Library.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class ReservationTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static Reservation Pending() => Reservation.Place(Guid.NewGuid(), Guid.NewGuid(), 1, Today);

    [Fact]
    public void Place_creates_a_pending_reservation_with_a_position()
    {
        var reservation = Reservation.Place(Guid.NewGuid(), Guid.NewGuid(), 2, Today);

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Equal(2, reservation.QueuePosition);
        Assert.True(reservation.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Place_with_a_bad_position_throws(int position) =>
        Assert.Throws<DomainException>(() => Reservation.Place(Guid.NewGuid(), Guid.NewGuid(), position, Today));

    [Fact]
    public void MarkReadyForPickup_holds_a_copy_and_sets_the_expiry()
    {
        var reservation = Pending();
        var copyId = Guid.NewGuid();

        reservation.MarkReadyForPickup(copyId, Today, Today.AddDays(3));

        Assert.Equal(ReservationStatus.ReadyForPickup, reservation.Status);
        Assert.Equal(copyId, reservation.HeldCopyId);
        Assert.Null(reservation.QueuePosition);
        Assert.Equal(Today.AddDays(3), reservation.ExpiresOn);
    }

    [Fact]
    public void Fulfilling_a_pending_reservation_throws() =>
        Assert.Throws<DomainException>(() => Pending().Fulfill());

    [Fact]
    public void Fulfilling_from_ready_completes_and_clears_the_held_copy()
    {
        var reservation = Pending();
        reservation.MarkReadyForPickup(Guid.NewGuid(), Today, Today.AddDays(3));

        reservation.Fulfill();

        Assert.Equal(ReservationStatus.Fulfilled, reservation.Status);
        Assert.Null(reservation.HeldCopyId);
        Assert.False(reservation.IsActive);
    }

    [Fact]
    public void Cancelling_an_already_inactive_reservation_throws()
    {
        var reservation = Pending();
        reservation.Cancel();

        Assert.Throws<DomainException>(() => reservation.Cancel());
    }

    [Fact]
    public void Expiring_a_pending_reservation_throws() =>
        Assert.Throws<DomainException>(() => Pending().Expire());
}
