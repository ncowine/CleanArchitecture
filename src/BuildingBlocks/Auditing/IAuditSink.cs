namespace BuildingBlocks.Auditing;

/// <summary>
/// Where audit records go. The capture point (the audit pipeline behavior) depends only on this, so the
/// destination is swappable: structured logs today, a different sink later — without touching call sites.
/// To send audit to Kibana, you don't even change this: keep the logging sink and add an Elasticsearch
/// sink to the logging pipeline (config only).
/// </summary>
public interface IAuditSink
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}
