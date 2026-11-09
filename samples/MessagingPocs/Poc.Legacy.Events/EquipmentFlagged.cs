using Common.RabbitMQ;

namespace Poc.Legacy.Events;

/// <summary>
/// A piece of equipment was flagged for attention. The JSON is {"Id":"...","Name":"..."}.
/// </summary>
/// <remarks>
/// This payload was a tuple, (Guid id, string name), until 2026-11-09. Tuples are rejected now: their items are fields,
/// which System.Text.Json doesn't serialize, so they arrived empty.
/// </remarks>
public class EquipmentFlagged : RemotePubSubEvent<EquipmentFlag>
{
}
