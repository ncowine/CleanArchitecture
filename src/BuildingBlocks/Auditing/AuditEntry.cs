namespace BuildingBlocks.Auditing;

/// <summary>
/// A single audit record: who did what, when, and how it turned out. Structured so that, once shipped
/// to a log store (e.g. Elasticsearch/Kibana), each field is independently searchable.
/// </summary>
public sealed record AuditEntry(
    string CorrelationId,
    string Actor,
    string Action,
    DateTime OccurredOnUtc,
    bool Succeeded,
    long ElapsedMs,
    string? Error);
