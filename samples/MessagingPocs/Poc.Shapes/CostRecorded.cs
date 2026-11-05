using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: decimals, one of them whole: the legacy format writes 12.0, the modern one 12.</summary>
[Message("Poc.Legacy.Events.Shapes.CostRecorded")]
public sealed class CostRecorded
{
    public decimal Amount { get; set; }

    public decimal Shipping { get; set; }

    public string Currency { get; set; } = "";

    public override string ToString() => FormattableString.Invariant($"{Amount} {Currency} + {Shipping} shipping");
}
