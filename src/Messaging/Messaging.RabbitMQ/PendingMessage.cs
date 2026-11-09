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

        public string RoutingKey { get; }

        public string MessageId { get; }

        public byte[] Body { get; }

        public DateTimeOffset CreatedUtc { get; }
    }
}
