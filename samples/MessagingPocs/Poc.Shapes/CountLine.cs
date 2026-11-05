namespace Poc.Shapes;

/// <summary>Part of <see cref="BatchCounted"/>; not a message by itself.</summary>
public sealed class CountLine
{
    public string Category { get; set; } = "";

    public int Count { get; set; }

    public override string ToString() => FormattableString.Invariant($"{Category}: {Count}");
}
