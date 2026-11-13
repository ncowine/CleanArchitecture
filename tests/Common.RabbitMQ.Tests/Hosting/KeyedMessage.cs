using Messaging;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>A second message type, for subscriptions that share a routing key in <see cref="HostingRoutingKeyTests"/>.</summary>
    [Message("Compat.Events.KeyedMessage")]
    public class KeyedMessage
    {
        public int Id { get; set; }
    }
}
