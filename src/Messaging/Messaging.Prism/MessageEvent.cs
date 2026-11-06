using System;
using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>
    /// The Prism event for a plain message class. One generic event covers every message, so no <c>PubSubEvent</c> class
    /// is written per message:
    /// <code><![CDATA[
    /// eventAggregator.GetEvent<MessageEvent<EmployeeSaved>>().Subscribe(OnSaved, ThreadOption.UIThread);
    /// eventAggregator.GetEvent<MessageEvent<EmployeeSaved>>().PublishRemote(saved);
    /// ]]></code>
    /// Subscribing starts receiving the message, so nothing else has to be registered, and it is dropped again when the
    /// last subscriber goes. A subscriber held weakly gives no signal when it is collected, so that one is noticed at the
    /// next message. To receive what a publisher sends with its own routing key, subscribe with <see cref="SubscribeOptions"/>:
    /// <code><![CDATA[
    /// eventAggregator.GetEvent<MessageEvent<EquipmentDisposed>>().Subscribe(OnDisposed,
    ///     new SubscribeOptions { Exchange = "Poc.Net472", RoutingKey = "equipment" }, ThreadOption.UIThread);
    /// ]]></code>
    /// </summary>
    public class MessageEvent<TMessage> : PubSubEvent<TMessage>, IReceivedEvent
    {
        private readonly KeyedSubscriptions keyed = new KeyedSubscriptions();

        /// <summary>
        /// As Prism's <c>Subscribe</c>, and binds the routing key in <paramref name="options"/> on the default bus until this
        /// subscription ends. The last subscription with that key and exchange removes the binding.
        /// </summary>
        public SubscriptionToken Subscribe(Action<TMessage> action, SubscribeOptions options, ThreadOption threadOption = ThreadOption.PublisherThread,
            bool keepSubscriberReferenceAlive = false, Predicate<TMessage> filter = null)
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
                MessageEventReceiver.Bind(this, options);
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
        public override SubscriptionToken Subscribe(Action<TMessage> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<TMessage> filter)
        {
            SubscriptionToken token = base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
            try
            {
                MessageEventReceiver.Subscribed(this);
            }
            catch
            {
                // Refused, for example a wire name another class uses: the caller gets no token, so nothing may stay.
                base.Unsubscribe(token);
                throw;
            }

            return token;
        }

        /// <summary>Disposing a subscription's token ends up here too.</summary>
        public override void Unsubscribe(SubscriptionToken token)
        {
            base.Unsubscribe(token);
            ReleaseEndedKeys();
            MessageEventReceiver.StopIfUnused(this);
        }

        public override void Unsubscribe(Action<TMessage> subscriber)
        {
            base.Unsubscribe(subscriber);
            ReleaseEndedKeys();
            MessageEventReceiver.StopIfUnused(this);
        }

        public override void Publish(TMessage payload)
        {
            base.Publish(payload);
            if (!keyed.IsEmpty)
            {
                ReleaseEndedKeys();
            }


            // Publishing has just removed collected subscribers. Almost every publish stops here, without the shared lock.
            if (Subscriptions.Count == 0)
            {
                MessageEventReceiver.StopIfUnused(this);
            }
        }

        /// <summary>Unbinds the keys of subscriptions that have ended, including collected ones.</summary>
        private void ReleaseEndedKeys()
        {
            Prune();
            foreach (SubscribeOptions options in keyed.RemoveEnded(Subscriptions))
            {
                MessageEventReceiver.Unbind(this, options);
            }
        }

        Type IReceivedEvent.MessageType => typeof(TMessage);

        bool IReceivedEvent.HasSubscribers
        {
            get
            {
                Prune();
                return Subscriptions.Count > 0;
            }
        }

        bool IReceivedEvent.BelongsTo(IEventAggregator eventAggregator)
        {
            return ReferenceEquals(eventAggregator.GetEvent<MessageEvent<TMessage>>(), this);
        }
    }
}
