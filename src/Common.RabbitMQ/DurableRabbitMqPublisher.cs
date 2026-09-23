using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ;

/// <summary>
/// Decorates the plain publish path with a local, disk-persisted retry queue — for hosts with no
/// database-backed outbox of their own (the WPF client). A message is staged in <see cref="PendingMessageStore"/>
/// before the network is touched and is only removed once a publish is actually confirmed, so a crash
/// mid-publish or the broker being unreachable never silently drops it. <see cref="RabbitMqOutboundRelay"/>
/// retries whatever is left, indefinitely — there is no attempt cap here, matching the durability this is
/// standing in for. Register via <c>AddDurablePublishing</c>; API hosts should keep the plain publisher
/// instead, since their outbox already provides this guarantee.
/// </summary>
internal sealed class DurableRabbitMqPublisher : IRabbitMqPublisher
{
    private readonly PendingMessageStore _store;
    private readonly IRawRabbitMqSender _sender;
    private readonly IMessageTypeRegistry _types;
    private readonly ITopologyRegistry _topology;
    private readonly ILogger<DurableRabbitMqPublisher> _logger;

    public DurableRabbitMqPublisher(
        PendingMessageStore store,
        IRawRabbitMqSender sender,
        IMessageTypeRegistry types,
        ITopologyRegistry topology,
        ILogger<DurableRabbitMqPublisher> logger)
    {
        _store = store;
        _sender = sender;
        _types = types;
        _topology = topology;
        _logger = logger;
    }

    public async Task PublishAsync<TMessage>(TMessage message, string? correlationId = null, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var messageType = _types.GetMessageType<TMessage>();
        var binding = _topology.GetPublishBinding<TMessage>();
        var raw = EnvelopeSerializer.ToRawMessage(message, messageType, binding, correlationId);

        var id = Guid.NewGuid();
        await _store.EnqueueAsync(id, raw, cancellationToken).ConfigureAwait(false);

        try
        {
            await _sender.SendAsync(raw, cancellationToken).ConfigureAwait(false);
            await _store.MarkDeliveredAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Already durable — the relay will retry. The caller asked for "accepted", not "delivered".
            RabbitMqLog.PublishFailed(_logger, exception, raw.Exchange);
            var nextAttempt = DateTimeOffset.UtcNow + RetryBackoff.Compute(1, OutboundRetryPolicy.BaseDelay, OutboundRetryPolicy.MaxDelay);
            await _store.MarkFailedAsync(id, exception.Message, nextAttempt, cancellationToken).ConfigureAwait(false);
        }
    }
}
