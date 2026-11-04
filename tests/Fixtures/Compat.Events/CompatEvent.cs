using Common.RabbitMQ;

namespace Compat.Events
{
    /// <summary>
    /// Compiled into Compat.Events (current build) and Compat.Events.Baseline (baseline build), so both sides
    /// have an event with the same FullName, which is the wire name. Only the baseline library needs the attribute.
    /// </summary>
#if COMPAT_BASELINE
    [RemoteEvent(CompatBus.Name)]
#endif
    public class CompatEvent : RemotePubSubEvent<CompatPayload>
    {
    }
}
