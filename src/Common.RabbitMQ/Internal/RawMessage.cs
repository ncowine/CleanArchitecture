namespace Common.RabbitMQ;

/// <summary>An already-serialized envelope, ready to hand to the broker. Used both for a fresh publish
/// and for replaying something the durable local queue is retrying — the two paths converge here.</summary>
internal sealed record RawMessage(
    string Exchange,
    RabbitExchangeType ExchangeType,
    string RoutingKey,
    string MessageType,
    string? CorrelationId,
    byte[] Body);
