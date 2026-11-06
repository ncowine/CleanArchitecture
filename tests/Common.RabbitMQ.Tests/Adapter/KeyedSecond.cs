using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Shares <see cref="KeyedFirst"/>'s routing key in <see cref="RemoteEventRoutingKeyTests"/>.</summary>
    public class KeyedSecond : RemotePubSubEvent<Order>
    {
    }
}
