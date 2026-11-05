namespace Poc.Shapes;

/// <summary>Part of <see cref="LocationMoved"/>; not a message by itself.</summary>
public sealed class Location
{
    public string Building { get; set; } = "";

    public int Floor { get; set; }

    public override string ToString() => FormattableString.Invariant($"{Building} floor {Floor}");
}
