using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: nested objects.</summary>
[Message("Poc.Legacy.Events.Shapes.LocationMoved")]
public sealed class LocationMoved
{
    public Location From { get; set; } = new Location();

    public Location To { get; set; } = new Location();

    public override string ToString() => $"{From} -> {To}";
}
