using Messaging.RabbitMQ;

namespace Common.RabbitMQ
{
    public static class MessageHeaders
    {
        public const string EventType = WireHeaders.MessageType;

        public const string SourceId = WireHeaders.SourceId;
    }
}
