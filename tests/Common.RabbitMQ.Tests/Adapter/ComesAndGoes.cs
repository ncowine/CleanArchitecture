using AppA.Events;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Gains and loses subscribers in <see cref="RemoteEventUnsubscribeTests"/>.</summary>
    public class ComesAndGoes : RemotePubSubEvent<Order>
    {
    }
}
