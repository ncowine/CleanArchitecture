using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Auditing;

/// <summary>
/// Default audit sink: writes each record as a structured log event. Because the fields are structured
/// (not interpolated into the message), they arrive as queryable properties in any structured log store.
/// To view audit in Kibana, add an Elasticsearch/OpenTelemetry sink to the logging pipeline — no change
/// here. To store audit durably/independently instead, swap this for a DB- or Elastic-backed IAuditSink.
/// </summary>
internal sealed class LoggingAuditSink : IAuditSink
{
    private readonly ILogger<LoggingAuditSink> _logger;

    public LoggingAuditSink(ILogger<LoggingAuditSink> logger)
    {
        _logger = logger;
    }

    [SuppressMessage(
        "Performance", "CA1873:Avoid potentially expensive logging",
        Justification = "Format is guarded by IsEnabled below; the analyzer doesn't track the guard " +
                        "through the source-generated LoggerMessage method.")]
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        // Flattening the details costs an allocation, so skip the whole record when the event would be
        // dropped anyway — audit runs on every auditable request, including the ones nobody is watching.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            AuditLog.Recorded(
                _logger,
                entry.Category,
                entry.CorrelationId,
                entry.Action,
                entry.Actor,
                entry.Succeeded,
                entry.ElapsedMs,
                entry.Source,
                entry.Resource,
                Format(entry.Details),
                entry.Error);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Flattens the custom details to <c>key=value; key=value</c> so they stay readable in a plain text
    /// log. Structured sinks (Elasticsearch) index the dictionary itself and never call this.
    /// </summary>
    private static string? Format(IReadOnlyDictionary<string, string?>? details)
        => details is null or { Count: 0 }
            ? null
            : string.Join("; ", details.Select(detail => $"{detail.Key}={detail.Value}"));
}

internal static partial class AuditLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "AUDIT({Category}) [{CorrelationId}] {Action} by {Actor} succeeded={Succeeded} " +
                  "in {ElapsedMs}ms source={Source} resource={Resource} details=[{Details}] {Error}")]
    public static partial void Recorded(
        ILogger logger,
        AuditCategory category,
        string correlationId,
        string action,
        string actor,
        bool succeeded,
        long elapsedMs,
        string? source,
        string? resource,
        string? details,
        string? error);
}
