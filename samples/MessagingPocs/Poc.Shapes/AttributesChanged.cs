using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: a dictionary: its keys keep their case.</summary>
[Message("Poc.Legacy.Events.Shapes.AttributesChanged")]
public sealed class AttributesChanged
{
    public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();

    public override string ToString() => $"{{{string.Join(", ", Attributes.Select(pair => $"{pair.Key}={pair.Value}"))}}}";
}
