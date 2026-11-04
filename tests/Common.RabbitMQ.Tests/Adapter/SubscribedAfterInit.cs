using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Subscribed to after the service started. Used by one test only.</summary>
    public class SubscribedAfterInit : RemotePubSubEvent<Order>
    {
    }
}
