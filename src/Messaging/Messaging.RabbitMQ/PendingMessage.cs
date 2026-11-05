using System;

namespace Messaging.RabbitMQ
{
    internal sealed class PendingMessage
    {
        public PendingMessage(string wireName, string routingKey, string messageId, byte[] body)
        {
            WireName = wireName;
            RoutingKey = routingKey;
            MessageId = messageId;
            Body = body;
            CreatedUtc = DateTimeOffset.UtcNow;
        }

        public string WireName { get; }

        /// <summary>The wire name unless the publisher chose another key.</summary>
        public string RoutingKey { get; }

        /// <summary>Sent as the message-id property.</summary>
        public string MessageId { get; }

        public byte[] Body { get; }

        public DateTimeOffset CreatedUtc { get; }
    }
}
