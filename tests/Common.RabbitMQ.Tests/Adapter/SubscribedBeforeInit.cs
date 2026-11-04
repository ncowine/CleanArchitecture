using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Subscribed to before the service started. Used by one test only.</summary>
    public class SubscribedBeforeInit : RemotePubSubEvent<Order>
    {
    }
}
