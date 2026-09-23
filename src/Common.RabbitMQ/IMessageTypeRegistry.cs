namespace Common.RabbitMQ;

/// <summary>
/// Maps a message's stable wire name (<see cref="MessageEnvelope.MessageType"/>) to the CLR type each
/// process deserializes it into, and back when publishing. Registration is explicit and local to each
/// process — see the note on <see cref="MessageEnvelope"/> for why nothing here is ever resolved by
/// reflecting a type name found on the wire.
/// </summary>
public interface IMessageTypeRegistry
{
    /// <summary>Registers the wire name for <typeparamref name="TMessage"/>. Call once per message type,
    /// at startup, in every process that publishes or consumes it.</summary>
    void Register<TMessage>(string messageType) where TMessage : class;

    /// <summary>The wire name registered for <typeparamref name="TMessage"/>.</summary>
    string GetMessageType<TMessage>() where TMessage : class;

    /// <summary>The wire name registered for <paramref name="clrType"/>.</summary>
    string GetMessageType(Type clrType);

    /// <summary>The CLR type registered for <paramref name="messageType"/>, or <see langword="null"/> if
    /// nothing in this process has registered it.</summary>
    Type? ResolveClrType(string messageType);
}
