using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>A remote event for tests that publish without a broker.</summary>
    public class OrderPlaced : RemotePubSubEvent<Order>
    {
    }
}
