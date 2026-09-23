using System.Collections.Concurrent;

namespace Common.RabbitMQ;

internal sealed class MessageTypeRegistry : IMessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _byMessageType = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _byClrType = new();

    public void Register<TMessage>(string messageType) where TMessage : class
    {
        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Message type name must not be empty.", nameof(messageType));
        }

        var clrType = typeof(TMessage);

        if (_byMessageType.TryGetValue(messageType, out var existingType) && existingType != clrType)
        {
            throw new InvalidOperationException(
                $"Message type '{messageType}' is already registered to '{existingType}'; cannot also register it to '{clrType}'.");
        }

        if (_byClrType.TryGetValue(clrType, out var existingMessageType) && existingMessageType != messageType)
        {
            throw new InvalidOperationException(
                $"'{clrType}' is already registered as message type '{existingMessageType}'; cannot also register it as '{messageType}'.");
        }

        _byMessageType[messageType] = clrType;
        _byClrType[clrType] = messageType;
    }

    public string GetMessageType<TMessage>() where TMessage : class => GetMessageType(typeof(TMessage));

    public string GetMessageType(Type clrType)
    {
        if (clrType is null)
        {
            throw new ArgumentNullException(nameof(clrType));
        }

        return _byClrType.TryGetValue(clrType, out var messageType)
            ? messageType
            : throw new InvalidOperationException(
                $"'{clrType}' has no registered message type. Call Register<{clrType.Name}>(...) at startup.");
    }

    public Type? ResolveClrType(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Message type name must not be empty.", nameof(messageType));
        }

        return _byMessageType.TryGetValue(messageType, out var clrType) ? clrType : null;
    }
}
