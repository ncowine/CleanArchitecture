using System;

namespace Common.RabbitMQ
{
    /// <summary>
    /// No longer used: the Messaging.RabbitMQ core buffers outgoing messages itself. Kept because it is public API
    /// that legacy apps may reference (ADR 0001, section 1).
    /// </summary>
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
