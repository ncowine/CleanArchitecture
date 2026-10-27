using Prism.Events;

namespace Poc.Legacy.Events;

/// <summary>
/// A piece of equipment was disposed of. Published by the net472 app; the API handles it (it deletes the equipment,
/// which sends EquipmentDeleted to everyone), and the other legacy apps see it too.
/// </summary>
/// <remarks>
/// The API's copy is Equipment.Infrastructure.IntegrationEvents.EquipmentDisposed, with
/// [Message("Poc.Legacy.Events.EquipmentDisposed")] — this class's full name. Renaming or moving this class changes
/// the wire name and silently disconnects the API.
/// </remarks>
public class EquipmentDisposed : PubSubEvent<EquipmentDisposal>
{
}
