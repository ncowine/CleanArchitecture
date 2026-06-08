namespace Students.Domain;

/// <summary>
/// A student's financial account — a ledger of charges, payments, and waivers with a running
/// <see cref="Balance"/>. Aggregate root, one per student (referenced by id). A positive balance is money
/// owed; a negative balance is a credit (e.g. an over-payment).
/// </summary>
public sealed class StudentAccount
{
    private readonly List<AccountEntry> _entries = [];

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public decimal Balance { get; private set; }

    public IReadOnlyCollection<AccountEntry> Entries => _entries.AsReadOnly();

    private StudentAccount() { }

    private StudentAccount(Guid id, Guid studentId)
    {
        Id = id;
        StudentId = studentId;
        Balance = 0m;
    }

    public static StudentAccount Open(Guid studentId)
    {
        if (studentId == Guid.Empty)
            throw new DomainException("An account must reference a student.");

        return new StudentAccount(Guid.NewGuid(), studentId);
    }

    public AccountEntry Charge(
        decimal amount, ChargeCategory category, string description, DateOnly occurredOn, Guid? sourceReference = null)
    {
        var entry = AddEntry(AccountEntryKind.Charge, category, amount, description, occurredOn, sourceReference);
        Balance += amount;
        return entry;
    }

    public AccountEntry RecordPayment(decimal amount, string description, DateOnly occurredOn)
    {
        var entry = AddEntry(AccountEntryKind.Payment, category: null, amount, description, occurredOn, sourceReference: null);
        Balance -= amount;
        return entry;
    }

    public AccountEntry Waive(decimal amount, string description, DateOnly occurredOn)
    {
        var entry = AddEntry(AccountEntryKind.Waiver, category: null, amount, description, occurredOn, sourceReference: null);
        Balance -= amount;
        return entry;
    }

    /// <summary>True once an entry from the given cross-module message has been posted (idempotency check).</summary>
    public bool HasEntryFrom(Guid sourceReference) =>
        _entries.Exists(entry => entry.SourceReference == sourceReference);

    private AccountEntry AddEntry(
        AccountEntryKind kind, ChargeCategory? category, decimal amount, string description, DateOnly occurredOn,
        Guid? sourceReference)
    {
        if (amount <= 0m)
            throw new DomainException("An amount must be positive.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("A description is required.");

        var entry = new AccountEntry(kind, category, amount, description.Trim(), occurredOn, sourceReference);
        _entries.Add(entry);
        return entry;
    }
}
