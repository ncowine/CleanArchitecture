namespace Common.RabbitMQ;

/// <summary>
/// Publishes directly, with no local durability of its own — a failed publish throws. This is the right
/// choice for a host that already has its own durable staging area (the API's transactional outbox):
/// layering a second retry mechanism underneath would just duplicate what the outbox already guarantees.
/// Hosts without one should register <see cref="DurableRabbitMqPublisher"/> instead (see
/// <c>AddDurablePublishing</c>).
/// </summary>
internal sealed class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly IRawRabbitMqSender _sender;
    private readonly IMessageTypeRegistry _types;
    private readonly ITopologyRegistry _topology;

    public RabbitMqPublisher(IRawRabbitMqSender sender, IMessageTypeRegistry types, ITopologyRegistry topology)
    {
        _sender = sender;
        _types = types;
        _topology = topology;
    }

    public Task PublishAsync<TMessage>(TMessage message, string? correlationId = null, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var messageType = _types.GetMessageType<TMessage>();
        var binding = _topology.GetPublishBinding<TMessage>();
        var raw = EnvelopeSerializer.ToRawMessage(message, messageType, binding, correlationId);

        return _sender.SendAsync(raw, cancellationToken);
    }
}
