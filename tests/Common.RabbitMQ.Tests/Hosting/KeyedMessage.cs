using Messaging;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Received by <see cref="KeyedMessageHandler"/>, whose <see cref="RoutingKeyAttribute"/> binds its key.</summary>
    [Message("Compat.Events.KeyedMessage")]
    public class KeyedMessage
    {
        public int Id { get; set; }
    }
}
