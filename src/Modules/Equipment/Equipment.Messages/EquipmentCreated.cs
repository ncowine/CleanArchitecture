using Messaging;

namespace Equipment.Messages;

/// <summary>A piece of equipment was added to inventory.</summary>
/// <remarks>
/// Rules for every class in this project, because other applications depend on them:
/// the wire name in <see cref="MessageAttribute"/> never changes; properties are never renamed or retyped (add new ones
/// instead); only plain get/set properties, so System.Text.Json and Newtonsoft read them the same way.
/// </remarks>
[Message("Equipment.EquipmentCreated")]
public sealed class EquipmentCreated
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string Category { get; set; } = "";

    public string AssetTag { get; set; } = "";

    public string Status { get; set; } = "";
}
