using Common.RabbitMQ;

namespace Poc.Legacy.Events;

/// <summary>
/// A piece of equipment was flagged for attention. Its payload is a tuple, to show that a legacy event like this works
/// with no change to it: on the wire the names are gone, so the JSON is {"Item1":"...","Item2":"..."}.
/// </summary>
/// <remarks>
/// Poc.Api.NoOutbox's copy maps Item1 and Item2 by position. Swapping the tuple's items here silently breaks it.
/// </remarks>
public class EquipmentFlagged : RemotePubSubEvent<(Guid id, string name)>
{
}
