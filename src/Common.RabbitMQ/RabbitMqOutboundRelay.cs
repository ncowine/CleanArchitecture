using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ;

/// <summary>
/// Periodically retries whatever <see cref="DurableRabbitMqPublisher"/> couldn't get confirmed
/// immediately. Runs only when <c>AddDurablePublishing</c> is registered.
/// </summary>
internal sealed class RabbitMqOutboundRelay : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;
    private const int StuckThreshold = 10;

    private readonly PendingMessageStore _store;
    private readonly IRawRabbitMqSender _sender;
    private readonly ILogger<RabbitMqOutboundRelay> _logger;

    public RabbitMqOutboundRelay(PendingMessageStore store, IRawRabbitMqSender sender, ILogger<RabbitMqOutboundRelay> logger)
    {
        _store = store;
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A plain delay loop, not PeriodicTimer — that type isn't available on net472.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
                await RelayBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                RabbitMqLog.RelayRetryFailed(_logger, exception, Guid.Empty, 0);
            }
        }
    }

    private async Task RelayBatchAsync(CancellationToken cancellationToken)
    {
        var pending = await _store.TakeBatchAsync(BatchSize, cancellationToken).ConfigureAwait(false);

        foreach (var message in pending)
        {
            var raw = new RawMessage(
                message.Exchange, message.ExchangeType, message.RoutingKey,
                message.MessageType, message.CorrelationId, message.Body);

            try
            {
                await _sender.SendAsync(raw, cancellationToken).ConfigureAwait(false);
                await _store.MarkDeliveredAsync(message.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var attempt = message.Attempts + 1;
                RabbitMqLog.RelayRetryFailed(_logger, exception, message.Id, attempt);

                var delay = RetryBackoff.Compute(attempt, OutboundRetryPolicy.BaseDelay, OutboundRetryPolicy.MaxDelay);
                await _store.MarkFailedAsync(message.Id, exception.Message, DateTimeOffset.UtcNow + delay, cancellationToken).ConfigureAwait(false);

                if (attempt >= StuckThreshold)
                {
                    RabbitMqLog.RelayStuck(_logger, message.Id, attempt);
                }
            }
        }
    }
}
