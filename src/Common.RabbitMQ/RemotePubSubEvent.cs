using System;
using Messaging;
using Prism.Events;

namespace Common.RabbitMQ
{
    public abstract class RemotePubSubEvent<TPayload> : PubSubEvent<TPayload>, IRemoteEvent
    {
        private readonly KeyedSubscriptions keyed = new KeyedSubscriptions();

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

        public override SubscriptionToken Subscribe(Action<TPayload> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<TPayload> filter)
        {
            SubscriptionToken token = base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
            RemoteEventSubscriptions.Subscribed(this);
            return token;
        }

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

            if (Subscriptions.Count == 0)
            {
                RemoteEventSubscriptions.StopIfUnused(this);
            }
        }

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
