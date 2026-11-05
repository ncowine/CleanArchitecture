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
    /// next message.
    /// </summary>
    public class MessageEvent<TMessage> : PubSubEvent<TMessage>, IReceivedEvent
    {
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
            MessageEventReceiver.StopIfUnused(this);
        }

        public override void Unsubscribe(Action<TMessage> subscriber)
        {
            base.Unsubscribe(subscriber);
            MessageEventReceiver.StopIfUnused(this);
        }

        public override void Publish(TMessage payload)
        {
            base.Publish(payload);

            // Publishing has just removed collected subscribers. Almost every publish stops here, without the shared lock.
            if (Subscriptions.Count == 0)
            {
                MessageEventReceiver.StopIfUnused(this);
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
