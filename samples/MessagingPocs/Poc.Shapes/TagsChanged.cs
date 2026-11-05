using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: a list of strings, including an empty one and one with non-ASCII text.</summary>
[Message("Poc.Legacy.Events.Shapes.TagsChanged")]
public sealed class TagsChanged
{
    public List<string> Tags { get; set; } = new List<string>();

    public override string ToString() => $"[{string.Join(", ", Tags.Select(tag => $"\"{tag}\""))}]";
}
