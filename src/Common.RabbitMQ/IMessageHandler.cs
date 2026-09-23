namespace Common.RabbitMQ;

/// <summary>Handles one message type received off a subscribed queue. Register one implementation per
/// message type in DI; the consumer resolves it per delivery, from a fresh scope.</summary>
public interface IMessageHandler<in TMessage> where TMessage : class
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>Envelope metadata handed alongside the deserialized payload.</summary>
public sealed record MessageContext(
    Guid MessageId, string MessageType, int SchemaVersion, string? CorrelationId, DateTimeOffset OccurredOnUtc, int DeliveryAttempt);
