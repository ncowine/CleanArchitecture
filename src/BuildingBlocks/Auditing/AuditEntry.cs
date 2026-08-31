namespace BuildingBlocks.Auditing;

/// <summary>
/// A single audit record: who did what, when, and how it turned out. Structured so that, once shipped
/// to a log store (e.g. Elasticsearch/Kibana), each field is independently searchable.
/// </summary>
/// <param name="Category">Write / Read / External / Security / Custom — see <see cref="AuditCategory"/>.</param>
/// <param name="Source">
/// Where the data lived, when it wasn't this module's own database: <c>"Api:CreditBureau"</c>,
/// <c>"Cache:students"</c>, <c>"File:\share\nightly.csv"</c>. Null means "our own DB".
/// </param>
/// <param name="Resource">What was touched, as an identifier a human can search on: <c>"Student/7f3…"</c>.</param>
/// <param name="Details">
/// Free-form facts the caller attached — the "custom" part of a custom audit record. Kept as strings so
/// one audit index can hold details from anywhere without mapping conflicts.
/// </param>
public sealed record AuditEntry(
    string CorrelationId,
    string Actor,
    string Action,
    DateTime OccurredOnUtc,
    bool Succeeded,
    long ElapsedMs,
    string? Error,
    IReadOnlyList<EntityChange> Changes,
    AuditCategory Category = AuditCategory.Write,
    string? Source = null,
    string? Resource = null,
    IReadOnlyDictionary<string, string?>? Details = null);
