using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Auditing.Elasticsearch;

/// <summary>Source-generated, allocation-free log messages for the Elasticsearch audit path.</summary>
internal static partial class AuditLogEvents
{
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Warning,
        Message = "Audit buffer full — Elasticsearch shipping is behind. Logging record instead: " +
                  "[{CorrelationId}] {Action} by {Actor} succeeded={Succeeded} changes={ChangeCount}")]
    public static partial void BufferFull(
        ILogger logger, string correlationId, string action, string actor, bool succeeded, int changeCount);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Failed to ship {Count} audit record(s) to Elasticsearch after {Retries} retries ({Reason}). " +
                  "Emitting to the log pipeline as fallback.")]
    public static partial void BatchFailed(ILogger logger, int count, int retries, string? reason);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Warning,
        Message = "AUDIT(unshipped) [{CorrelationId}] {Action} by {Actor} succeeded={Succeeded} changes={ChangeCount}")]
    public static partial void Unshipped(
        ILogger logger, string correlationId, string action, string actor, bool succeeded, int changeCount);
}
