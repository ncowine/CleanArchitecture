using Common.RabbitMQ;

namespace Compat.Events
{
    /// <summary>
    /// Compiled into Compat.Events (current build) and Compat.Events.Baseline (baseline build), so both sides
    /// have an event with the same FullName, which is the wire name.
    /// </summary>
    [RemoteEvent(CompatBus.Name)]
    public class CompatEvent : RemotePubSubEvent<CompatPayload>
    {
    }
}
