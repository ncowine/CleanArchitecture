using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: long, double and bool.</summary>
[Message("Poc.Legacy.Events.Shapes.UsageReported")]
public sealed class UsageReported
{
    public long Hours { get; set; }

    public double TemperatureC { get; set; }

    public bool Overheated { get; set; }

    public override string ToString() => FormattableString.Invariant($"{Hours} h, {TemperatureC} °C, overheated {Overheated}");
}
