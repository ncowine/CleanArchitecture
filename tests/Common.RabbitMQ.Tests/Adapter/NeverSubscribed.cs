using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Nothing subscribes to it, so nothing binds it. Used by one test only.</summary>
    public class NeverSubscribed : RemotePubSubEvent<Order>
    {
    }
}
