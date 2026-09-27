using System.Text.Json;
using BuildingBlocks.Correlation;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.EntityFrameworkCore;
using MessagingCorrelation = Messaging.Hosting.CorrelationContext;

namespace BuildingBlocks.Outbox.Messaging;

/// <summary>
/// Sends <typeparamref name="TContext"/>'s outbox messages to RabbitMQ (docs/messaging/adr/0003). Each row is turned
/// back into its message class and published with <see cref="IConfirmedMessagePublisher"/>, so the outbox marks it
/// delivered only after the broker has confirmed it. A broker outage defers the row instead of using up its
/// attempts. The row's ID travels as the AMQP message-id, so a message sent again keeps the same ID, and the
/// correlation ID of the request that wrote the row travels as the correlation-id header (with telemetry on).
/// <para>
/// A module whose outbox also drives in-process steps (like Onboarding's saga) can hand the rows it doesn't
/// recognise to this dispatcher: check <see cref="CanDispatch"/>, then call <see cref="DispatchAsync"/>.
/// </para>
/// </summary>
public sealed class MessagingOutboxDispatcher<TContext> : IOutboxDispatcher<TContext> where TContext : DbContext
{
    private readonly IConfirmedMessagePublisher _publisher;
    private readonly OutboxMessageTypes<TContext> _messageTypes;
    private readonly ICorrelationContext _correlation;

    public MessagingOutboxDispatcher(IConfirmedMessagePublisher publisher, OutboxMessageTypes<TContext> messageTypes, ICorrelationContext correlation)
    {
        _publisher = publisher;
        _messageTypes = messageTypes;
        _correlation = correlation;
    }

    public bool CanDispatch(string type) => _messageTypes.TryGet(type, out _);

    public async Task DispatchAsync(Guid messageId, string type, string content, CancellationToken cancellationToken)
    {
        if (!_messageTypes.TryGet(type, out var messageType))
        {
            throw new InvalidOperationException(
                $"Unknown outbox message type '{type}'. Add it to AddOutboxPublishing<{typeof(TContext).Name}>().");
        }

        // The same default System.Text.Json settings OutboxWriter used to store it.
        var message = JsonSerializer.Deserialize(content, messageType)
            ?? throw new InvalidOperationException($"Outbox message {messageId} ({type}) has no content.");

        // The outbox processor has restored the row's correlation ID into this scope's ICorrelationContext. The
        // messaging library reads its own ambient context, so carry the ID across for the publish.
        using var correlation = MessagingCorrelation.Begin(_correlation.CorrelationId);

        try
        {
            await _publisher.PublishConfirmedAsync(message, messageId.ToString("N"), cancellationToken);
        }
        catch (BrokerUnavailableException exception)
        {
            throw new OutboxDeliveryDeferredException(exception.Message, exception);
        }
    }
}
