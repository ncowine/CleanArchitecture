using System;

namespace Legacy.RabbitMQ
{
    public sealed class OutgoingMessage
    {
        public OutgoingMessage(string eventName, byte[] body)
        {
            EventName = eventName;
            Body = body;
            MessageId = Guid.NewGuid().ToString("N");
            CreatedUtc = DateTimeOffset.UtcNow;
        }

        public string EventName { get; }

        public byte[] Body { get; }

        public string MessageId { get; }

        public DateTimeOffset CreatedUtc { get; }
    }
}
