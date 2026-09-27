using Messaging;

namespace Equipment.Messages;

/// <summary>A piece of equipment was removed from inventory.</summary>
[Message("Equipment.EquipmentDeleted")]
public sealed class EquipmentDeleted
{
    public Guid Id { get; set; }
}
