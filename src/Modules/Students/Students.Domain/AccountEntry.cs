namespace Students.Domain;

/// <summary>
/// A single line on a <see cref="StudentAccount"/> ledger — a charge, payment, or waiver. Part of the
/// account aggregate; created only through it, so construction is <c>internal</c>. <see cref="Amount"/> is
/// always positive; the <see cref="Kind"/> decides whether it raises or lowers the balance.
/// </summary>
public sealed class AccountEntry
{
    public Guid Id { get; private set; }
    public AccountEntryKind Kind { get; private set; }

    /// <summary>The kind of charge; null for payments and waivers.</summary>
    public ChargeCategory? Category { get; private set; }

    public decimal Amount { get; private set; }
    public string Description { get; private set; } = null!;
    public DateOnly OccurredOn { get; private set; }

    /// <summary>
    /// The id of the originating cross-module message, when this entry came from one (e.g. a library fine).
    /// Used as an idempotency key so an at-least-once event isn't charged twice. Null for direct entries.
    /// </summary>
    public Guid? SourceReference { get; private set; }

    private AccountEntry() { }

    internal AccountEntry(
        AccountEntryKind kind, ChargeCategory? category, decimal amount, string description, DateOnly occurredOn,
        Guid? sourceReference)
    {
        Id = Guid.NewGuid();
        Kind = kind;
        Category = category;
        Amount = amount;
        Description = description;
        OccurredOn = occurredOn;
        SourceReference = sourceReference;
    }
}
