using System;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Base class for events that travel between applications over RabbitMQ.
    /// Use <see cref="RemotePubSubEventExtensions.PublishRemote{TPayload}"/> to send; subscribe as with any Prism event.
    /// Nothing has to be registered: subscribing is what binds the queue, so an application only receives what it listens
    /// for. When the last subscriber goes, the binding is removed. A subscriber held weakly gives no signal when it is
    /// collected, so that one is noticed at the next message. Bindings change in the background, so a message sent right
    /// after subscribing, or while nothing is subscribed, is missed.
    /// <para>Events that stay inside the application derive from <see cref="PubSubEvent{TPayload}"/> directly.</para>
    /// </summary>
    public abstract class RemotePubSubEvent<TPayload> : PubSubEvent<TPayload>, IRemoteEvent
    {
        /// <summary>Prism's other <c>Subscribe</c> overloads all end up here.</summary>
        public override SubscriptionToken Subscribe(Action<TPayload> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<TPayload> filter)
        {
            SubscriptionToken token = base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
            RemoteEventSubscriptions.Subscribed(this);
            return token;
        }

        /// <summary>Disposing a subscription's token ends up here too.</summary>
        public override void Unsubscribe(SubscriptionToken token)
        {
            base.Unsubscribe(token);
            RemoteEventSubscriptions.StopIfUnused(this);
        }

        public override void Unsubscribe(Action<TPayload> subscriber)
        {
            base.Unsubscribe(subscriber);
            RemoteEventSubscriptions.StopIfUnused(this);
        }

        public override void Publish(TPayload payload)
        {
            base.Publish(payload);

            // Publishing has just removed collected subscribers. Almost every publish stops here, without the shared lock.
            if (Subscriptions.Count == 0)
            {
                RemoteEventSubscriptions.StopIfUnused(this);
            }
        }

        bool IRemoteEvent.HasSubscribers
        {
            get
            {
                Prune();
                return Subscriptions.Count > 0;
            }
        }
    }
}
