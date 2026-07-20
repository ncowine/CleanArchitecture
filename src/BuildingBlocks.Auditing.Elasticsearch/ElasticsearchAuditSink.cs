using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Auditing.Elasticsearch;

/// <summary>
/// Audit sink that hands each record to the background shipper via <see cref="AuditShipmentQueue"/>.
/// The call is non-blocking and never throws, so a slow or unreachable Elasticsearch can never delay or
/// fail the business command being audited. If the buffer is full, the record is logged instead of lost.
/// </summary>
internal sealed class ElasticsearchAuditSink : IAuditSink
{
    private readonly AuditShipmentQueue _queue;
    private readonly ILogger<ElasticsearchAuditSink> _logger;

    public ElasticsearchAuditSink(AuditShipmentQueue queue, ILogger<ElasticsearchAuditSink> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        if (!_queue.TryEnqueue(entry))
        {
            AuditLogEvents.BufferFull(
                _logger, entry.CorrelationId, entry.Action, entry.Actor, entry.Succeeded, entry.Changes.Count);
        }

        return Task.CompletedTask;
    }
}
