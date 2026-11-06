using System;
using Messaging;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Base class for events that travel between applications over RabbitMQ.
    /// Use <see cref="RemotePubSubEventExtensions.PublishRemote{TPayload}"/> to send; subscribe as with any Prism event.
    /// Nothing has to be registered. The queue is bound once per exchange, so every event on it arrives; subscribing is
    /// what stops this one being dropped, and when the last subscriber goes it is dropped again. A subscriber held weakly
    /// gives no signal when it is collected, so that one is noticed at the next message.
    /// <para>To receive what a publisher sends with its own routing key, subscribe with <see cref="SubscribeOptions"/>:</para>
    /// <code><![CDATA[
    /// eventAggregator.GetEvent<EquipmentDisposed>().Subscribe(OnDisposed,
    ///     new SubscribeOptions { Exchange = "Poc.Net472", RoutingKey = "equipment" }, ThreadOption.UIThread);
    /// ]]></code>
    /// <para>Events that stay inside the application derive from <see cref="PubSubEvent{TPayload}"/> directly.</para>
    /// </summary>
    public abstract class RemotePubSubEvent<TPayload> : PubSubEvent<TPayload>, IRemoteEvent
    {
        private readonly KeyedSubscriptions keyed = new KeyedSubscriptions();

        /// <summary>
        /// As Prism's <c>Subscribe</c>, and binds the routing key in <paramref name="options"/> on the default bus until this
        /// subscription ends. The last subscription with that key and exchange removes the binding.
        /// </summary>
        public SubscriptionToken Subscribe(Action<TPayload> action, SubscribeOptions options, ThreadOption threadOption = ThreadOption.PublisherThread,
            bool keepSubscriberReferenceAlive = false, Predicate<TPayload> filter = null)
        {
            if (options != null && string.IsNullOrWhiteSpace(options.RoutingKey))
            {
                throw new ArgumentException("SubscribeOptions needs a RoutingKey.", nameof(options));
            }

            SubscriptionToken token = Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter ?? (_ => true));
            if (options == null)
            {
                return token;
            }

            try
            {
                RemoteEventSubscriptions.Bind(this, options);
            }
            catch
            {
                Unsubscribe(token);
                throw;
            }

            keyed.Add(token, options);
            return token;
        }

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
            ReleaseEndedKeys();
            RemoteEventSubscriptions.StopIfUnused(this);
        }

        public override void Unsubscribe(Action<TPayload> subscriber)
        {
            base.Unsubscribe(subscriber);
            ReleaseEndedKeys();
            RemoteEventSubscriptions.StopIfUnused(this);
        }

        public override void Publish(TPayload payload)
        {
            base.Publish(payload);
            if (!keyed.IsEmpty)
            {
                ReleaseEndedKeys();
            }


            // Publishing has just removed collected subscribers. Almost every publish stops here, without the shared lock.
            if (Subscriptions.Count == 0)
            {
                RemoteEventSubscriptions.StopIfUnused(this);
            }
        }

        /// <summary>Unbinds the keys of subscriptions that have ended, including collected ones.</summary>
        private void ReleaseEndedKeys()
        {
            Prune();
            foreach (SubscribeOptions options in keyed.RemoveEnded(Subscriptions))
            {
                RemoteEventSubscriptions.Unbind(this, options);
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
