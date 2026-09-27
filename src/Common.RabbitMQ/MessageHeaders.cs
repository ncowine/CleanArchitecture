using Messaging.RabbitMQ;

namespace Common.RabbitMQ
{
    public static class MessageHeaders
    {
        /// <summary>Full name of the Prism event type, used to resolve the event on the receiving side.</summary>
        public const string EventType = WireHeaders.MessageType;

        /// <summary>Id of the sending <see cref="RabbitMQService"/>, used to drop our own messages.</summary>
        public const string SourceId = WireHeaders.SourceId;
    }
}
