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

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        AuditLog.Recorded(
            _logger, entry.CorrelationId, entry.Action, entry.Actor, entry.Succeeded, entry.ElapsedMs, entry.Error);
        return Task.CompletedTask;
    }
}

internal static partial class AuditLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "AUDIT [{CorrelationId}] {Action} by {Actor} succeeded={Succeeded} in {ElapsedMs}ms {Error}")]
    public static partial void Recorded(
        ILogger logger, string correlationId, string action, string actor, bool succeeded, long elapsedMs, string? error);
}
