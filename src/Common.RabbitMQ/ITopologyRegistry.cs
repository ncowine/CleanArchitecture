namespace Common.RabbitMQ;

/// <summary>Explicit publish-side routing per message type — declared once at startup, never inferred.</summary>
public interface ITopologyRegistry
{
    void MapPublish<TMessage>(PublishBinding binding) where TMessage : class;

    PublishBinding GetPublishBinding<TMessage>() where TMessage : class;

    PublishBinding GetPublishBinding(Type clrType);
}
