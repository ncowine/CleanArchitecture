using System;

namespace Messaging
{
    public interface IMessageSerializer
    {
        /// <summary>Sent as the content-type property of every message.</summary>
        string ContentType { get; }

        /// <summary>Serializes <paramref name="message"/> using its runtime type.</summary>
        byte[] Serialize(object message);

        object Deserialize(byte[] body, Type type);
    }
}
