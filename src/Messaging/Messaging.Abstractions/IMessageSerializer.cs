using System;

namespace Messaging
{
    /// <summary>Turns messages into message bodies and back. Each bus has its own serializer.</summary>
    public interface IMessageSerializer
    {
        /// <summary>Sent as the content-type property of every message.</summary>
        string ContentType { get; }

        /// <summary>Serializes <paramref name="message"/> using its runtime type.</summary>
        byte[] Serialize(object message);

        object Deserialize(byte[] body, Type type);
    }
}
