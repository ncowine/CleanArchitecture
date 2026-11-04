using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Subscribed to through another application's aggregator. Used by one test only.</summary>
    public class SubscribedInAnotherApp : RemotePubSubEvent<Order>
    {
    }
}
