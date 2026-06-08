namespace Library.Domain;

public enum ReservationStatus
{
    /// <summary>In the queue, waiting for a copy to free up.</summary>
    Pending,

    /// <summary>A copy has been held on the hold shelf; the student has until the expiry to pick it up.</summary>
    ReadyForPickup,

    /// <summary>Picked up (a loan was issued).</summary>
    Fulfilled,
    Cancelled,

    /// <summary>The pickup window passed without collection.</summary>
    Expired,
}
