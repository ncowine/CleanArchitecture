using System;

namespace Messaging.RabbitMQ
{
    /// <summary>A serialized message waiting in the outgoing buffer.</summary>
    internal sealed class PendingMessage
    {
        public PendingMessage(PublishContext context, byte[] body)
        {
            Context = context;
            Body = body;
            CreatedUtc = DateTimeOffset.UtcNow;
        }

        /// <summary>Wire name, message ID and the observer's extra headers.</summary>
        public PublishContext Context { get; }

        public byte[] Body { get; }

        public DateTimeOffset CreatedUtc { get; }
    }
}
