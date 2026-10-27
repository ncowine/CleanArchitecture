using Prism.Events;

// The namespace is part of the wire name: "Equipment" + "EquipmentUpdated" = the API's [Message("Equipment.EquipmentUpdated")].
namespace Equipment;

/// <summary>The API's EquipmentUpdated, as a legacy Prism event. Payload: the API's own message class.</summary>
public class EquipmentUpdated : PubSubEvent<Messages.EquipmentUpdated>
{
}
