using RabbitMQ.Client;

namespace Common.RabbitMQ;

/// <summary>The one place <see cref="RabbitExchangeType"/> is mapped onto RabbitMQ.Client's own exchange
/// type strings — shared by the publisher and the consumer so a new exchange kind only needs adding here.</summary>
internal static class AmqpExchangeType
{
    public static string ToAmqpString(this RabbitExchangeType type) => type switch
    {
        RabbitExchangeType.Direct => ExchangeType.Direct,
        RabbitExchangeType.Fanout => ExchangeType.Fanout,
        RabbitExchangeType.Topic => ExchangeType.Topic,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
    };
}
