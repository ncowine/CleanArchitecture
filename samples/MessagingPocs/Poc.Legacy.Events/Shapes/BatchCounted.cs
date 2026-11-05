using Common.RabbitMQ;

// The namespace is part of the wire name: it must match [Message("Poc.Legacy.Events.Shapes.BatchCounted")] on the payload.
namespace Poc.Legacy.Events.Shapes;

public class BatchCounted : RemotePubSubEvent<Poc.Shapes.BatchCounted>
{
}
