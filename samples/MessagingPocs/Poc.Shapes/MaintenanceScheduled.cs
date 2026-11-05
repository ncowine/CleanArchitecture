using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: a UTC DateTime and a DateTimeOffset with its offset.</summary>
[Message("Poc.Legacy.Events.Shapes.MaintenanceScheduled")]
public sealed class MaintenanceScheduled
{
    public DateTime DueUtc { get; set; }

    public DateTimeOffset BookedAt { get; set; }

    public override string ToString() => FormattableString.Invariant($"due {DueUtc:O} ({DueUtc.Kind}), booked {BookedAt:O}");
}
