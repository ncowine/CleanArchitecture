using Prism.Events;

// The namespace is part of the wire name: "Equipment" + "EquipmentCreated" = the API's [Message("Equipment.EquipmentCreated")].
namespace Equipment;

/// <summary>The API's EquipmentCreated, as a legacy Prism event. Payload: the API's own message class.</summary>
public class EquipmentCreated : PubSubEvent<Messages.EquipmentCreated>
{
}
