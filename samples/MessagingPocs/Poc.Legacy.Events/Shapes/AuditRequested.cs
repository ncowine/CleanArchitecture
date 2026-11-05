using Common.RabbitMQ;

// The namespace is part of the wire name: it must match [Message("Poc.Legacy.Events.Shapes.AuditRequested")] on the payload.
namespace Poc.Legacy.Events.Shapes;

public class AuditRequested : RemotePubSubEvent<Poc.Shapes.AuditRequested>
{
}
