using Common.RabbitMQ;

// The namespace is part of the wire name: it must match [Message("Poc.Legacy.Events.Shapes.WarrantyChecked")] on the payload.
namespace Poc.Legacy.Events.Shapes;

public class WarrantyChecked : RemotePubSubEvent<Poc.Shapes.WarrantyChecked>
{
}
