using Common.RabbitMQ;

// The namespace is part of the wire name: it must match [Message("Poc.Legacy.Events.Shapes.LocationMoved")] on the payload.
namespace Poc.Legacy.Events.Shapes;

public class LocationMoved : RemotePubSubEvent<Poc.Shapes.LocationMoved>
{
}
