using System.Text.Json;

namespace Common.RabbitMQ;

/// <summary>Builds the wire bytes for a message. Shared by the plain and durable publishers so both
/// produce byte-for-byte the same envelope shape.</summary>
internal static class EnvelopeSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RawMessage ToRawMessage<TMessage>(
        TMessage message,
        string messageType,
        PublishBinding binding,
        string? correlationId)
        where TMessage : class
    {
        var envelope = new MessageEnvelope(
            Guid.NewGuid(),
            messageType,
            SchemaVersion: 1,
            correlationId,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(message, JsonOptions));

        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

        return new RawMessage(binding.Exchange, binding.ExchangeType, binding.RoutingKey, messageType, correlationId, body);
    }
}
