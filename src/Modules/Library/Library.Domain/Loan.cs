namespace Library.Domain;

/// <summary>
/// A book loan owned entirely by the Library app's own database. It references a <see cref="BookCopy"/> by
/// <see cref="CopyId"/> and a student by <see cref="StudentId"/> (the key from the main Students database).
/// There is deliberately no cross-database foreign key for the student — keeping that integrity (the
/// student exists) is the application's job. The copy lives in the same database, but is a separate
/// aggregate, so it too is referenced by id only.
/// </summary>
public sealed class Loan
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid CopyId { get; private set; }
    public DateOnly BorrowedOn { get; private set; }
    public DateOnly DueOn { get; private set; }
    public DateOnly? ReturnedOn { get; private set; }
    public decimal FineAmount { get; private set; }
    public int RenewalCount { get; private set; }

    public bool IsReturned => ReturnedOn is not null;

    private Loan() { }

    private Loan(Guid id, Guid studentId, Guid copyId, DateOnly borrowedOn, DateOnly dueOn)
    {
        Id = id;
        StudentId = studentId;
        CopyId = copyId;
        BorrowedOn = borrowedOn;
        DueOn = dueOn;
    }

    public static Loan Borrow(Guid studentId, Guid copyId, DateOnly borrowedOn, DateOnly dueOn)
    {
        if (studentId == Guid.Empty)
            throw new DomainException("A loan must reference a student.");
        if (copyId == Guid.Empty)
            throw new DomainException("A loan must reference a copy.");
        if (dueOn <= borrowedOn)
            throw new DomainException("The due date must be after the borrow date.");

        return new Loan(Guid.NewGuid(), studentId, copyId, borrowedOn, dueOn);
    }

    /// <summary>
    /// Returns the loan, accruing an overdue fine when returned past the due date. Returns the fine that
    /// was added (0 when on time). Returning an already-returned loan is an error.
    /// </summary>
    public decimal Return(DateOnly returnedOn, decimal finePerDayOverdue)
    {
        if (IsReturned)
            throw new DomainException("This loan has already been returned.");

        ReturnedOn = returnedOn;

        var overdueDays = returnedOn.DayNumber - DueOn.DayNumber;
        if (overdueDays <= 0)
        {
            return 0m;
        }

        var overdueFine = overdueDays * finePerDayOverdue;
        FineAmount += overdueFine;
        return overdueFine;
    }

    /// <summary>Extends the due date, up to the renewal limit. Cannot renew a returned loan.</summary>
    public void Renew(int extensionDays, int maxRenewals)
    {
        if (IsReturned)
            throw new DomainException("A returned loan cannot be renewed.");
        if (RenewalCount >= maxRenewals)
            throw new DomainException("This loan has reached its renewal limit.");

        DueOn = DueOn.AddDays(extensionDays);
        RenewalCount++;
    }

    /// <summary>
    /// Adds a fine to this loan (damage, manual penalty, etc.). Fines accumulate; crossing a cumulative
    /// limit is what later triggers a hold in the main Students database, via the outbox.
    /// </summary>
    public void AssessFine(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("A fine amount must be positive.");

        FineAmount += amount;
    }

    /// <summary>
    /// Writes off this loan's fine. Used as the compensating action when a hold the fine triggered is
    /// rejected by the main system. Idempotent — waiving an already-zero fine is a no-op.
    /// </summary>
    public void WaiveFine()
    {
        FineAmount = 0m;
    }
}
