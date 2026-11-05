using Common.RabbitMQ;

// The namespace is part of the wire name: it must match [Message("Poc.Legacy.Events.Shapes.TagsChanged")] on the payload.
namespace Poc.Legacy.Events.Shapes;

public class TagsChanged : RemotePubSubEvent<Poc.Shapes.TagsChanged>
{
}
