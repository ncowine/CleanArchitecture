using System;

namespace Messaging.RabbitMQ
{
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
