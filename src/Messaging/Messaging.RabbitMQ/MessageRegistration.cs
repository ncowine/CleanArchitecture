using System;

namespace Messaging.RabbitMQ
{
    /// <summary>A message a bus receives: its wire name (the routing key it binds) and the type its body deserializes to.</summary>
    public sealed class MessageRegistration
    {
        public MessageRegistration(string wireName, Type messageType)
        {
            if (string.IsNullOrWhiteSpace(wireName))
            {
                throw new ArgumentException("A wire name is required.", nameof(wireName));
            }

            WireName = wireName;
            MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        }

        public string WireName { get; }

        public Type MessageType { get; }
    }
}
