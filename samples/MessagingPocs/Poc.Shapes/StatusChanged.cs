using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: an enum, sent as its number.</summary>
[Message("Poc.Legacy.Events.Shapes.StatusChanged")]
public sealed class StatusChanged
{
    public Guid EquipmentId { get; set; }

    public EquipmentState Status { get; set; }

    public override string ToString() => FormattableString.Invariant($"{EquipmentId} is now {Status} ({(int)Status})");
}
