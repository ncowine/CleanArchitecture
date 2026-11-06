using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Subscribed with a routing key in <see cref="RemoteEventRoutingKeyTests"/>.</summary>
    public class KeyedFirst : RemotePubSubEvent<Order>
    {
    }
}
