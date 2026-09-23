namespace Common.RabbitMQ;

/// <summary>Publishes a message under its registered wire name and topology binding.</summary>
public interface IRabbitMqPublisher
{
    Task PublishAsync<TMessage>(TMessage message, string? correlationId = null, CancellationToken cancellationToken = default)
        where TMessage : class;
}
