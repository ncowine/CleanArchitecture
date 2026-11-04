using System;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Base class for events that travel between applications over RabbitMQ.
    /// Use <see cref="RemotePubSubEventExtensions.PublishRemote{TPayload}"/> to send; subscribe as with any Prism event.
    /// Nothing has to be registered: subscribing is what binds the queue, so an application only receives what it listens
    /// for. The binding is created in the background and stays when the subscriber goes away.
    /// <para>Events that stay inside the application derive from <see cref="PubSubEvent{TPayload}"/> directly.</para>
    /// </summary>
    public abstract class RemotePubSubEvent<TPayload> : PubSubEvent<TPayload>
    {
        /// <summary>Prism's other <c>Subscribe</c> overloads all end up here.</summary>
        public override SubscriptionToken Subscribe(Action<TPayload> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<TPayload> filter)
        {
            SubscriptionToken token = base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
            RemoteEventSubscriptions.Subscribed(this);
            return token;
        }
    }
}
