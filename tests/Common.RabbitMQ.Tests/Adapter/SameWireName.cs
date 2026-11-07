using AppA.Events;
using Messaging;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Claims <see cref="OrderPlaced"/>'s wire name, which the registry must refuse.</summary>
    [Message("Common.RabbitMQ.Tests.Adapter.OrderPlaced")]
    public class SameWireName : RemotePubSubEvent<Order>
    {
    }
}
