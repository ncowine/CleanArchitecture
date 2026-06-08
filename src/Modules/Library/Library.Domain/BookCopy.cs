namespace Library.Domain;

/// <summary>
/// A physical copy of a <see cref="Book"/>. Aggregate root — references the book by id only. Its
/// <see cref="Status"/> is what circulation moves through: an available copy can be loaned, a loaned copy
/// returned, and a copy can be marked lost or withdrawn from the collection.
/// </summary>
public sealed class BookCopy
{
    public Guid Id { get; private set; }
    public Guid BookId { get; private set; }
    public string Barcode { get; private set; } = null!;
    public CopyCondition Condition { get; private set; }
    public CopyStatus Status { get; private set; }
    public DateOnly AcquiredOn { get; private set; }

    public bool IsAvailable => Status == CopyStatus.Available;

    private BookCopy() { }

    private BookCopy(Guid id, Guid bookId, string barcode, CopyCondition condition, DateOnly acquiredOn)
    {
        Id = id;
        BookId = bookId;
        Barcode = barcode;
        Condition = condition;
        AcquiredOn = acquiredOn;
        Status = CopyStatus.Available;
    }

    public static BookCopy Create(Guid bookId, string barcode, CopyCondition condition, DateOnly acquiredOn)
    {
        if (bookId == Guid.Empty)
            throw new DomainException("A copy must reference a book.");
        if (string.IsNullOrWhiteSpace(barcode))
            throw new DomainException("A barcode is required.");

        return new BookCopy(Guid.NewGuid(), bookId, barcode.Trim().ToUpperInvariant(), condition, acquiredOn);
    }

    /// <summary>Available → on loan. Used by circulation when the copy is borrowed.</summary>
    public void MarkOnLoan()
    {
        if (Status != CopyStatus.Available)
            throw new DomainException($"A {Status} copy cannot be loaned.");

        Status = CopyStatus.OnLoan;
    }

    /// <summary>On loan → available. Used by circulation when the copy is returned.</summary>
    public void MarkReturned()
    {
        if (Status != CopyStatus.OnLoan)
            throw new DomainException("Only a copy that is on loan can be returned.");

        Status = CopyStatus.Available;
    }

    public void MarkLost()
    {
        if (Status == CopyStatus.Withdrawn)
            throw new DomainException("A withdrawn copy cannot be marked lost.");

        Status = CopyStatus.Lost;
    }

    public void Withdraw()
    {
        if (Status is CopyStatus.OnLoan or CopyStatus.Reserved)
            throw new DomainException($"A {Status} copy cannot be withdrawn.");

        Status = CopyStatus.Withdrawn;
    }

    /// <summary>Available → reserved. Holds a returned copy on the hold shelf for a reservation.</summary>
    public void HoldForPickup()
    {
        if (Status != CopyStatus.Available)
            throw new DomainException("Only an available copy can be held for pickup.");

        Status = CopyStatus.Reserved;
    }

    /// <summary>Reserved → on loan. Issues a held copy to the reservation that's picking it up.</summary>
    public void LendReserved()
    {
        if (Status != CopyStatus.Reserved)
            throw new DomainException("Only a copy held for pickup can be lent to its reservation.");

        Status = CopyStatus.OnLoan;
    }

    /// <summary>Reserved → available. Releases a held copy when its reservation is cancelled or expires.</summary>
    public void ReleaseHold()
    {
        if (Status != CopyStatus.Reserved)
            throw new DomainException("Only a copy held for pickup can be released.");

        Status = CopyStatus.Available;
    }

    public void UpdateCondition(CopyCondition condition) => Condition = condition;
}
