namespace BuildingBlocks.Auditing;

/// <summary>
/// Per-request accumulator for entity changes. The EF <c>SaveChanges</c> interceptor fills this while a
/// command executes; the audit behavior reads it afterwards and attaches the changes to the audit record.
/// Scoped, so each command's changes stay isolated to its own DI scope.
/// </summary>
public interface IAuditScope
{
    void Add(EntityChange change);

    IReadOnlyList<EntityChange> Changes { get; }
}

internal sealed class AuditScope : IAuditScope
{
    private readonly List<EntityChange> _changes = [];

    public void Add(EntityChange change) => _changes.Add(change);

    public IReadOnlyList<EntityChange> Changes => _changes;
}
