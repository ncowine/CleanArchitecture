using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: a list of objects.</summary>
[Message("Poc.Legacy.Events.Shapes.BatchCounted")]
public sealed class BatchCounted
{
    public List<CountLine> Lines { get; set; } = new List<CountLine>();

    public override string ToString() => $"[{string.Join(", ", Lines)}]";
}
