using Messaging;
using Shared.Events;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>An adapter app's own copy of the legacy <c>Shared.Events.UserChanged</c>, matched by wire name.</summary>
    [Message("Shared.Events.UserChanged")]
    public class RemoteUserChanged : RemotePubSubEvent<User>
    {
    }
}
