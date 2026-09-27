using System.Text.Json;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox.Messaging;

/// <summary>
/// Sends <typeparamref name="TContext"/>'s outbox messages to RabbitMQ (docs/messaging/adr/0003). Each row is turned
/// back into its message class and published with <see cref="IConfirmedMessagePublisher"/>, so the outbox marks it
/// delivered only after the broker has confirmed it. A broker outage defers the row instead of using up its
/// attempts. The row's ID travels as the AMQP message-id, so a message sent again keeps the same ID.
/// <para>
/// A module whose outbox also drives in-process steps (like Onboarding's saga) can hand the rows it doesn't
/// recognise to this dispatcher: check <see cref="CanDispatch"/>, then call <see cref="DispatchAsync"/>.
/// </para>
/// </summary>
public sealed class MessagingOutboxDispatcher<TContext> : IOutboxDispatcher<TContext> where TContext : DbContext
{
    private readonly IConfirmedMessagePublisher _publisher;
    private readonly OutboxMessageTypes<TContext> _messageTypes;

    public MessagingOutboxDispatcher(IConfirmedMessagePublisher publisher, OutboxMessageTypes<TContext> messageTypes)
    {
        _publisher = publisher;
        _messageTypes = messageTypes;
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
