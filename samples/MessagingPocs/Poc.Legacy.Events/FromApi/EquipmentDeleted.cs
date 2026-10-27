using Prism.Events;

// The namespace is part of the wire name: "Equipment" + "EquipmentDeleted" = the API's [Message("Equipment.EquipmentDeleted")].
namespace Equipment;

/// <summary>The API's EquipmentDeleted, as a legacy Prism event. Payload: the API's own message class.</summary>
public class EquipmentDeleted : PubSubEvent<Messages.EquipmentDeleted>
{
}
