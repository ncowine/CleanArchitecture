namespace Library.Domain;

/// <summary>
/// A student's place in the hold queue for a <see cref="Book"/>. Aggregate root — references the book,
/// student, and (once a copy is held) the copy by id only. While <see cref="ReservationStatus.Pending"/>
/// it carries a <see cref="QueuePosition"/>; once a copy frees up it moves to
/// <see cref="ReservationStatus.ReadyForPickup"/> with the held copy and a pickup expiry.
/// </summary>
public sealed class Reservation
{
    public Guid Id { get; private set; }
    public Guid BookId { get; private set; }
    public Guid StudentId { get; private set; }
    public ReservationStatus Status { get; private set; }

    /// <summary>1-based place in line while <see cref="ReservationStatus.Pending"/>; null otherwise.</summary>
    public int? QueuePosition { get; private set; }

    /// <summary>The copy held on the shelf while <see cref="ReservationStatus.ReadyForPickup"/>; null otherwise.</summary>
    public Guid? HeldCopyId { get; private set; }

    public DateOnly ReservedOn { get; private set; }
    public DateOnly? ReadyOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }

    public bool IsActive => Status is ReservationStatus.Pending or ReservationStatus.ReadyForPickup;

    private Reservation() { }

    private Reservation(Guid id, Guid bookId, Guid studentId, int queuePosition, DateOnly reservedOn)
    {
        Id = id;
        BookId = bookId;
        StudentId = studentId;
        QueuePosition = queuePosition;
        ReservedOn = reservedOn;
        Status = ReservationStatus.Pending;
    }

    public static Reservation Place(Guid bookId, Guid studentId, int queuePosition, DateOnly reservedOn)
    {
        if (bookId == Guid.Empty)
            throw new DomainException("A reservation must reference a book.");
        if (studentId == Guid.Empty)
            throw new DomainException("A reservation must reference a student.");
        if (queuePosition < 1)
            throw new DomainException("Queue position must be at least 1.");

        return new Reservation(Guid.NewGuid(), bookId, studentId, queuePosition, reservedOn);
    }

    /// <summary>Pending → ready: a copy has been held on the shelf for this reservation.</summary>
    public void MarkReadyForPickup(Guid heldCopyId, DateOnly readyOn, DateOnly expiresOn)
    {
        if (Status != ReservationStatus.Pending)
            throw new DomainException("Only a pending reservation can be made ready for pickup.");

        Status = ReservationStatus.ReadyForPickup;
        HeldCopyId = heldCopyId;
        QueuePosition = null;
        ReadyOn = readyOn;
        ExpiresOn = expiresOn;
    }

    /// <summary>Ready → fulfilled: the held copy was picked up (a loan was issued).</summary>
    public void Fulfill()
    {
        if (Status != ReservationStatus.ReadyForPickup)
            throw new DomainException("Only a ready-for-pickup reservation can be fulfilled.");

        Status = ReservationStatus.Fulfilled;
        HeldCopyId = null;
    }

    public void Cancel()
    {
        if (!IsActive)
            throw new DomainException("Only an active reservation can be cancelled.");

        Status = ReservationStatus.Cancelled;
        QueuePosition = null;
        HeldCopyId = null;
    }

    /// <summary>Ready → expired: the pickup window passed without collection.</summary>
    public void Expire()
    {
        if (Status != ReservationStatus.ReadyForPickup)
            throw new DomainException("Only a ready-for-pickup reservation can expire.");

        Status = ReservationStatus.Expired;
        HeldCopyId = null;
    }

    public void SetQueuePosition(int position)
    {
        if (Status == ReservationStatus.Pending)
        {
            QueuePosition = position;
        }
    }
}
