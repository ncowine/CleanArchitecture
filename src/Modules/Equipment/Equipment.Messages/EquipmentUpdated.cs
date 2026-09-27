using Messaging;

namespace Equipment.Messages;

/// <summary>A piece of equipment changed. Carries its full current state, so applying it twice is harmless.</summary>
[Message("Equipment.EquipmentUpdated")]
public sealed class EquipmentUpdated
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public string AssetTag { get; set; } = "";

    public string Status { get; set; } = "";
}
