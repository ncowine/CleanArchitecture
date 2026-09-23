namespace Common.RabbitMQ;

/// <summary>The actual network call. Throws on anything short of a broker-confirmed publish — never
/// swallows a failure — so both callers (the plain publisher and the durable relay) can decide for
/// themselves what "not delivered yet" means for them.</summary>
internal interface IRawRabbitMqSender
{
    Task SendAsync(RawMessage message, CancellationToken cancellationToken);
}
