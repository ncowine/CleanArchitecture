namespace Common.RabbitMQ;

/// <summary>AMQP 0-9-1 exchange kinds this library supports.</summary>
public enum RabbitExchangeType
{
    Direct,
    Fanout,
    Topic,
}
