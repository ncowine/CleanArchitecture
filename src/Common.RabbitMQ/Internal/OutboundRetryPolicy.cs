namespace Common.RabbitMQ;

/// <summary>Backoff policy for the durable outbound queue — shared by <see cref="DurableRabbitMqPublisher"/>
/// (the first failed attempt) and <see cref="RabbitMqOutboundRelay"/> (every attempt after) so both
/// schedule a pending message's next try the same way. No cap on attempt count, by design: an outbound
/// message here has nowhere else to go, so it waits for the broker rather than being dropped.</summary>
internal static class OutboundRetryPolicy
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);
}
