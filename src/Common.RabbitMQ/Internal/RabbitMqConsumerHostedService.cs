using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Common.RabbitMQ;

/// <summary>
/// One background service handling every registered <see cref="QueueSubscription"/>. Manual ack
/// throughout: a delivery is acknowledged only once its handler has actually run. On failure, the message
/// is parked in a per-subscription retry queue with a per-message TTL (exponential backoff — see
/// <see cref="RetryBackoff"/>); that queue has no consumer of its own, so nothing happens to a message
/// sitting in it except the broker expiring it once its TTL elapses and dead-lettering it straight back
/// into the original queue for another attempt. No timer or poll loop in this process drives that delay —
/// the broker does. Once <see cref="QueueSubscription.MaxDeliveryAttempts"/> is exhausted the message is
/// nacked without requeue — dead-lettered to the subscription's own configured DLX if it has one, dropped
/// by the broker otherwise.
/// </summary>
internal sealed class RabbitMqConsumerHostedService : BackgroundService
{
    private const string AttemptHeader = "x-attempt";
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethodsByMessageType = new();

    private readonly IRabbitMqConnectionProvider _connections;
    private readonly IMessageTypeRegistry _types;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<QueueSubscription> _subscriptions;
    private readonly ProcessedMessageStore? _processedMessages;
    private readonly ILogger<RabbitMqConsumerHostedService> _logger;
    private readonly List<IChannel> _channels = [];

    // Tracks deliveries currently mid-handler so a shutdown can wait for them instead of dropping
    // whatever was in flight — see StopAsync.
    private int _inFlight;

    public RabbitMqConsumerHostedService(
        IRabbitMqConnectionProvider connections,
        IMessageTypeRegistry types,
        IServiceScopeFactory scopeFactory,
        IEnumerable<QueueSubscription> subscriptions,
        ILogger<RabbitMqConsumerHostedService> logger,
        ProcessedMessageStore? processedMessages = null)
    {
        _connections = connections;
        _types = types;
        _scopeFactory = scopeFactory;
        _subscriptions = subscriptions.ToList();
        _logger = logger;
        _processedMessages = processedMessages;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var subscription in _subscriptions)
        {
            await StartConsumingAsync(subscription, stoppingToken).ConfigureAwait(false);
        }

        // Everything from here runs event-driven off AsyncEventingBasicConsumer; just hold the service open.
        // TaskCompletionSource<object?>, not the non-generic form — that overload isn't available on net472.
        var completion = new TaskCompletionSource<object?>();
        using (stoppingToken.Register(() => completion.TrySetResult(null)))
        {
            await completion.Task.ConfigureAwait(false);
        }
    }

    private async Task StartConsumingAsync(QueueSubscription subscription, CancellationToken cancellationToken)
    {
        var channel = await _connections.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _channels.Add(channel);

        await DeclareTopologyAsync(channel, subscription, cancellationToken).ConfigureAwait(false);
        await channel.BasicQosAsync(prefetchSize: 0, subscription.PrefetchCount, global: false, cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnDeliveredAsync(channel, subscription, delivery, cancellationToken);

        await channel.BasicConsumeAsync(subscription.Queue, autoAck: false, consumer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The private, consumer-less queue a failed delivery waits in until its backoff TTL
    /// expires and the broker dead-letters it back to <see cref="QueueSubscription.Queue"/>.</summary>
    private static string RetryQueueName(QueueSubscription subscription) => $"{subscription.Queue}.retry";

    private static async Task DeclareTopologyAsync(IChannel channel, QueueSubscription subscription, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            subscription.Exchange, subscription.ExchangeType.ToAmqpString(), durable: true, autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // No exchange or binding needed for the retry queue itself: it's reached only via the default
        // exchange (routing key = queue name), and it dead-letters straight back to the real exchange —
        // explicitly with the original routing key, since without x-dead-letter-routing-key the broker
        // would otherwise reuse the routing key the message entered THIS queue with (its own name).
        if (subscription.MaxDeliveryAttempts > 1)
        {
            await channel.QueueDeclareAsync(
                RetryQueueName(subscription), durable: true, exclusive: false, autoDelete: false,
                new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = subscription.Exchange,
                    ["x-dead-letter-routing-key"] = subscription.RoutingKey,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var arguments = new Dictionary<string, object?>();
        if (subscription.DeadLetterExchange is not null)
        {
            arguments["x-dead-letter-exchange"] = subscription.DeadLetterExchange;

            await channel.ExchangeDeclareAsync(
                subscription.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (subscription.DeadLetterQueue is not null)
            {
                await channel.QueueDeclareAsync(
                    subscription.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await channel.QueueBindAsync(
                    subscription.DeadLetterQueue, subscription.DeadLetterExchange, routingKey: string.Empty,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        await channel.QueueDeclareAsync(
            subscription.Queue, subscription.Durable, exclusive: false, autoDelete: false, arguments,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.QueueBindAsync(
            subscription.Queue, subscription.Exchange, subscription.RoutingKey, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task OnDeliveredAsync(IChannel channel, QueueSubscription subscription, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var attempt = GetAttempt(delivery.BasicProperties);
        Interlocked.Increment(ref _inFlight);

        try
        {
            using var document = JsonDocument.Parse(delivery.Body.ToArray());
            var envelope = document.RootElement;
            var messageId = envelope.TryGetProperty("messageId", out var idElement) ? idElement.GetGuid() : Guid.Empty;
            var messageType = envelope.GetProperty("messageType").GetString()
                ?? throw new InvalidOperationException("Message envelope had no messageType.");
            var schemaVersion = envelope.TryGetProperty("schemaVersion", out var schemaVersionElement) ? schemaVersionElement.GetInt32() : 1;
            var correlationId = envelope.TryGetProperty("correlationId", out var correlationElement) && correlationElement.ValueKind != JsonValueKind.Null
                ? correlationElement.GetString()
                : null;
            var occurredOnUtc = envelope.TryGetProperty("occurredOnUtc", out var occurredElement)
                ? occurredElement.GetDateTimeOffset()
                : DateTimeOffset.UtcNow;
            var payloadElement = envelope.GetProperty("payload");

            var clrType = _types.ResolveClrType(messageType);
            if (clrType is null)
            {
                RabbitMqLog.UnknownMessageType(_logger, messageType, subscription.Queue);
                RabbitMqDiagnostics.ConsumeFailed.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Opt-in dedup (AddInboxDeduplication): checked here, before the handler runs, but only ever
            // MARKED after the handler succeeds (below) — never here. Marking it processed this early
            // would mean a transient handler failure's own retry finds the message already "processed"
            // and skips the handler forever on redelivery, silently losing it instead of retrying it.
            if (_processedMessages is not null && messageId != Guid.Empty
                && await _processedMessages.IsProcessedAsync(messageId, cancellationToken).ConfigureAwait(false))
            {
                RabbitMqDiagnostics.ConsumeDuplicate.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            var payload = payloadElement.Deserialize(clrType, JsonOptions)
                ?? throw new InvalidOperationException($"Message '{messageType}' deserialized to null.");

            var context = new MessageContext(messageId, messageType, schemaVersion, correlationId, occurredOnUtc, attempt);

            using var scope = _scopeFactory.CreateScope();
            var handlerType = typeof(IMessageHandler<>).MakeGenericType(clrType);
            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler is null)
            {
                RabbitMqLog.NoHandlerRegistered(_logger, messageType, subscription.Queue);
                RabbitMqDiagnostics.ConsumeFailed.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            var handleMethod = HandleMethodsByMessageType.GetOrAdd(
                clrType, static t => typeof(IMessageHandler<>).MakeGenericType(t).GetMethod(nameof(IMessageHandler<object>.HandleAsync))!);
            await ((Task)handleMethod.Invoke(handler, [payload, context, cancellationToken])!).ConfigureAwait(false);

            // Marked processed only now that the handler has actually succeeded — see the comment above
            // on the pre-handler check for why marking it any earlier would be a bug, not an optimization.
            if (_processedMessages is not null && messageId != Guid.Empty)
            {
                await _processedMessages.MarkProcessedAsync(messageId, cancellationToken).ConfigureAwait(false);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
            RabbitMqDiagnostics.Consumed.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RabbitMqDiagnostics.ConsumeFailed.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
            await HandleFailureAsync(channel, subscription, delivery, attempt, exception, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task HandleFailureAsync(
        IChannel channel, QueueSubscription subscription, BasicDeliverEventArgs delivery, int attempt, Exception exception, CancellationToken cancellationToken)
    {
        RabbitMqLog.DeliveryFailed(_logger, exception, subscription.Queue, attempt);

        if (attempt < subscription.MaxDeliveryAttempts)
        {
            var headers = delivery.BasicProperties.Headers is not null
                ? new Dictionary<string, object?>(delivery.BasicProperties.Headers)
                : new Dictionary<string, object?>();
            headers[AttemptHeader] = attempt + 1;

            var delay = RetryBackoff.Compute(attempt, subscription.RetryBaseDelay, subscription.RetryMaxDelay);
            var properties = new BasicProperties(delivery.BasicProperties)
            {
                Headers = headers,
                Expiration = ((long)delay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            };

            // Published to the retry queue via the default exchange (routing key = queue name — no
            // binding needed), not straight back to the original queue: this is what turns "retry" into
            // "retry after a delay" without any timer of our own. See DeclareTopologyAsync.
            await channel.BasicPublishAsync(
                exchange: string.Empty, routingKey: RetryQueueName(subscription), mandatory: false, properties, delivery.Body, cancellationToken)
                .ConfigureAwait(false);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
            RabbitMqDiagnostics.Retried.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
        }
        else
        {
            RabbitMqLog.DeadLettered(_logger, subscription.Queue, attempt);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken).ConfigureAwait(false);
            RabbitMqDiagnostics.DeadLettered.Add(1, new KeyValuePair<string, object?>("queue", subscription.Queue));
        }
    }

    private static int GetAttempt(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is not null && properties.Headers.TryGetValue(AttemptHeader, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.Parse(Encoding.UTF8.GetString(bytes), CultureInfo.InvariantCulture),
                _ => 1,
            };
        }

        return 1;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Give in-flight handlers a chance to finish (and ack) before the channels they'd ack through are
        // closed out from under them — otherwise a deploy mid-delivery both fails the handler's ack AND
        // relies on the broker's own redelivery-on-disconnect to pick it back up, which works but is a
        // needless extra retry for something that was about to succeed anyway.
        var deadline = DateTime.UtcNow + DrainTimeout;
        while (Volatile.Read(ref _inFlight) > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var channel in _channels)
        {
            await channel.CloseAsync(cancellationToken).ConfigureAwait(false);
            channel.Dispose();
        }
    }
}
