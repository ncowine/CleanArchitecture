using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Base class for events that travel between applications over RabbitMQ.
    /// Use <see cref="RemotePubSubEventExtensions.PublishRemote{TPayload}"/> to send; subscribe as with any Prism event.
    /// <para>Events that stay inside the application derive from <see cref="PubSubEvent{TPayload}"/> directly.</para>
    /// </summary>
    public abstract class RemotePubSubEvent<TPayload> : PubSubEvent<TPayload>
    {
    }
}
