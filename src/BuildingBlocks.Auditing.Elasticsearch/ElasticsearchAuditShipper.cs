using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Auditing.Elasticsearch;

/// <summary>
/// Background worker that drains the audit queue and bulk-indexes records into Elasticsearch, batched
/// for throughput and retried with backoff. If Elasticsearch stays unreachable past the retry budget, the
/// batch is written to the log pipeline as a fallback so the audit trail is never silently dropped.
/// </summary>
internal sealed class ElasticsearchAuditShipper : BackgroundService
{
    private readonly AuditShipmentQueue _queue;
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchAuditOptions _options;
    private readonly ILogger<ElasticsearchAuditShipper> _logger;

    public ElasticsearchAuditShipper(
        AuditShipmentQueue queue,
        ElasticsearchClient client,
        IOptions<ElasticsearchAuditOptions> options,
        ILogger<ElasticsearchAuditShipper> logger)
    {
        _queue = queue;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var batch = new List<AuditEntry>(_options.BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < _options.BatchSize && reader.TryRead(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count > 0)
                {
                    await ShipAsync(batch, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down — expected.
        }
    }

    private async Task ShipAsync(IReadOnlyCollection<AuditEntry> batch, CancellationToken cancellationToken)
    {
        // Records may span days (index rolls over daily), so bulk per target index.
        foreach (var group in batch.GroupBy(IndexFor))
        {
            var documents = group.ToList();

            for (var attempt = 1; ; attempt++)
            {
                string? reason;
                try
                {
                    var response = await _client
                        .BulkAsync(bulk => bulk.Index(group.Key).IndexMany(documents), cancellationToken)
                        .ConfigureAwait(false);

                    if (response.IsValidResponse && !response.Errors)
                    {
                        break;
                    }

                    reason = response.DebugInformation;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    reason = exception.Message;
                }

                if (attempt > _options.MaxRetries)
                {
                    FallbackLog(documents, reason);
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string IndexFor(AuditEntry entry)
        => string.Format(CultureInfo.InvariantCulture, _options.IndexFormat, entry.OccurredOnUtc);

    private void FallbackLog(List<AuditEntry> documents, string? reason)
    {
        AuditLogEvents.BatchFailed(_logger, documents.Count, _options.MaxRetries, reason);

        foreach (var entry in documents)
        {
            AuditLogEvents.Unshipped(
                _logger, entry.CorrelationId, entry.Action, entry.Actor, entry.Succeeded, entry.Changes.Count);
        }
    }
}
