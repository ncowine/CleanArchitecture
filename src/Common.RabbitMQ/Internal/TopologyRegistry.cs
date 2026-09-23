using System.Collections.Concurrent;

namespace Common.RabbitMQ;

internal sealed class TopologyRegistry : ITopologyRegistry
{
    private readonly ConcurrentDictionary<Type, PublishBinding> _bindings = new();

    public void MapPublish<TMessage>(PublishBinding binding) where TMessage : class
    {
        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        _bindings[typeof(TMessage)] = binding;
    }

    public PublishBinding GetPublishBinding<TMessage>() where TMessage : class => GetPublishBinding(typeof(TMessage));

    public PublishBinding GetPublishBinding(Type clrType)
    {
        if (clrType is null)
        {
            throw new ArgumentNullException(nameof(clrType));
        }

        return _bindings.TryGetValue(clrType, out var binding)
            ? binding
            : throw new InvalidOperationException(
                $"'{clrType}' has no publish binding registered. Call MapPublish<{clrType.Name}>(...) at startup.");
    }
}
