namespace Library.Domain;

/// <summary>
/// A book loan owned entirely by the Library app's own database. It references a student only by
/// <see cref="StudentId"/> — the key from the main Students database. There is deliberately no
/// cross-database foreign key: <see cref="StudentId"/> is just a value, and keeping referential
/// integrity (the student exists, hasn't been deleted) is the application's job, not the database's.
/// </summary>
public sealed class Loan
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public string BookTitle { get; private set; } = null!;
    public DateOnly BorrowedOn { get; private set; }
    public DateOnly DueOn { get; private set; }
    public DateOnly? ReturnedOn { get; private set; }
    public decimal FineAmount { get; private set; }

    private Loan() { }

    private Loan(Guid id, Guid studentId, string bookTitle, DateOnly borrowedOn, DateOnly dueOn)
    {
        Id = id;
        StudentId = studentId;
        BookTitle = bookTitle;
        BorrowedOn = borrowedOn;
        DueOn = dueOn;
    }

    public static Loan Borrow(Guid studentId, string bookTitle, DateOnly borrowedOn, DateOnly dueOn)
    {
        if (studentId == Guid.Empty)
            throw new DomainException("A loan must reference a student.");
        if (string.IsNullOrWhiteSpace(bookTitle))
            throw new DomainException("Book title is required.");
        if (dueOn <= borrowedOn)
            throw new DomainException("The due date must be after the borrow date.");

        return new Loan(Guid.NewGuid(), studentId, bookTitle.Trim(), borrowedOn, dueOn);
    }

    /// <summary>
    /// Adds a fine to this loan (late return, damage, etc.). Fines accumulate; crossing a cumulative
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
