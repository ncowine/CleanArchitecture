using System.Globalization;

namespace BuildingBlocks.Auditing;

/// <summary>
/// Per-request accumulator for what the audit behavior should attach to the current request's record:
/// entity changes (filled by the EF <c>SaveChanges</c> interceptor) and free-form annotations (added by
/// handlers and adapters). Scoped, so each request's material stays isolated to its own DI scope.
/// Handlers normally reach this through <see cref="IAuditRecorder.Annotate"/> rather than directly.
/// </summary>
public interface IAuditScope
{
    void Add(EntityChange change);

    IReadOnlyList<EntityChange> Changes { get; }

    /// <summary>
    /// Attaches a custom fact to the audit record for the request in flight — e.g. which upstream API
    /// answered, how many rows came back, which cache tier served the read. Last write wins per key.
    /// Values are redacted and truncated by <see cref="AuditRedaction"/> on the way in, and the number
    /// of details is capped, so no caller can make a record unbounded or leak a secret into it.
    /// </summary>
    void Annotate(string key, string? value);

    /// <summary>The annotations collected so far. Empty unless something called <see cref="Annotate"/>.</summary>
    IReadOnlyDictionary<string, string?> Details { get; }
}

internal sealed class AuditScope : IAuditScope
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyDetails =
        new Dictionary<string, string?>(StringComparer.Ordinal);

    // One request is normally one thread, but not always: a handler that fans out with Task.WhenAll over
    // two DbContexts has the interceptor and the handler writing here at once. Corrupting a List under
    // that race would lose audit records for the reason hardest to reproduce, so the notepad locks.
    private readonly Lock _gate = new();
    private readonly List<EntityChange> _changes = [];
    private readonly Dictionary<string, string?> _details = new(StringComparer.Ordinal);
    private int _dropped;

    public void Add(EntityChange change)
    {
        lock (_gate)
        {
            _changes.Add(change);
        }
    }

    // Snapshots, so a reader is never iterating a collection a background continuation is still writing to.
    public IReadOnlyList<EntityChange> Changes
    {
        get
        {
            lock (_gate)
            {
                return _changes.ToArray();
            }
        }
    }

    public void Annotate(string key, string? value)
    {
        lock (_gate)
        {
            // Updating a key already present is always allowed; only new keys are capped, so a loop
            // annotating the same fact repeatedly can't be what pushes a record over the limit.
            if (!_details.ContainsKey(key) && _details.Count >= AuditRedaction.MaxDetails)
            {
                _dropped++;
                return;
            }

            _details[key] = AuditRedaction.Sanitize(key, value);
        }
    }

    public IReadOnlyDictionary<string, string?> Details
    {
        get
        {
            lock (_gate)
            {
                if (_details.Count == 0 && _dropped == 0)
                {
                    return EmptyDetails;
                }

                var snapshot = new Dictionary<string, string?>(_details, StringComparer.Ordinal);
                if (_dropped > 0)
                {
                    snapshot[AuditRedaction.DroppedKey] = _dropped.ToString(CultureInfo.InvariantCulture);
                }

                return snapshot;
            }
        }
    }
}
