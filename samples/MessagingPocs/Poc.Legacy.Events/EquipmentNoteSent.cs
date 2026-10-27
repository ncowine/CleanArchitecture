using Prism.Events;

namespace Poc.Legacy.Events;

/// <summary>
/// A note about a piece of equipment, from one desktop app to the others. App to app only: the API doesn't listen.
/// Wire name: "Poc.Legacy.Events.EquipmentNoteSent" (the full type name, as for every legacy event).
/// </summary>
public class EquipmentNoteSent : PubSubEvent<EquipmentNote>
{
}
