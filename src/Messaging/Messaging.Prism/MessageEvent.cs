using System;
using Prism.Events;

namespace Messaging.Prism
{
    public class MessageEvent<TMessage> : PubSubEvent<TMessage>, IReceivedEvent
    {
        private readonly KeyedSubscriptions keyed = new KeyedSubscriptions();

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

        public override SubscriptionToken Subscribe(Action<TMessage> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<TMessage> filter)
        {
            SubscriptionToken token = base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
            try
            {
                MessageEventReceiver.Subscribed(this);
            }
            catch
            {
                base.Unsubscribe(token);
                throw;
            }

            return token;
        }

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

            if (Subscriptions.Count == 0)
            {
                MessageEventReceiver.StopIfUnused(this);
            }
        }

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
