namespace Common.RabbitMQ;

/// <summary>
/// Where a message type is published: which exchange, of what kind, with what routing key. Declared
/// explicitly per message type via <see cref="ITopologyRegistry.MapPublish{TMessage}"/> — never inferred
/// from whether a routing key happens to be supplied.
/// </summary>
public sealed record PublishBinding(string Exchange, RabbitExchangeType ExchangeType, string RoutingKey);
