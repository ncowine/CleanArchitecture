namespace Common.RabbitMQ;

/// <summary>
/// A queue this process consumes from, and the exchange binding that feeds it. The consumer declares the
/// exchange, the queue and the binding itself on startup — nothing needs to be pre-provisioned out of
/// band, though pointing two processes at the same already-declared topology (e.g. a legacy exchange) is
/// equally fine since declaration is idempotent.
/// </summary>
public sealed record QueueSubscription(string Queue, string Exchange, RabbitExchangeType ExchangeType, string RoutingKey)
{
    public bool Durable { get; init; } = true;

    /// <summary>Delivery attempts (the first try counts as 1) before a message is nacked without requeue
    /// — dead-lettered if <see cref="DeadLetterExchange"/> is set, dropped by the broker otherwise.</summary>
    public int MaxDeliveryAttempts { get; init; } = 3;

    /// <summary>Fanout exchange the queue dead-letters to once <see cref="MaxDeliveryAttempts"/> is
    /// exhausted, or a delivery names a message type nothing in this process has registered.</summary>
    public string? DeadLetterExchange { get; init; }

    /// <summary>Queue bound to <see cref="DeadLetterExchange"/>, declared alongside it. Only meaningful
    /// when <see cref="DeadLetterExchange"/> is set.</summary>
    public string? DeadLetterQueue { get; init; }

    /// <summary>Delay before the first redelivery attempt. Doubles on each subsequent failure, capped at
    /// <see cref="RetryMaxDelay"/>. Redelivery is a broker-side TTL + dead-letter hop — see
    /// <c>RabbitMqConsumerHostedService.DeclareTopologyAsync</c> — so this needs no timer in-process.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How many unacknowledged deliveries this queue's consumer will hold at once.</summary>
    public ushort PrefetchCount { get; init; } = 10;
}
