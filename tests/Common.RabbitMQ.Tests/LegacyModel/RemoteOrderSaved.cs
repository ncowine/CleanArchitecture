using AppA.Events;
using Messaging;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// An adapter app's own copy of the legacy <c>AppA.Events.OrderSaved</c>: no shared event assembly, only the wire name
    /// matches.
    /// </summary>
    [Message("AppA.Events.OrderSaved")]
    public class RemoteOrderSaved : RemotePubSubEvent<Order>
    {
    }
}
