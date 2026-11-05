using Messaging;

namespace Poc.Shapes;

/// <summary>Shape: no properties at all: the body is {}.</summary>
[Message("Poc.Legacy.Events.Shapes.AuditRequested")]
public sealed class AuditRequested
{
    public override string ToString() => "(no properties)";
}
