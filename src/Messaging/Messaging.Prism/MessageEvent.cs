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
    /// Subscribing starts receiving the message, so nothing else has to be registered.
    /// </summary>
    public class MessageEvent<TMessage> : PubSubEvent<TMessage>
    {
        /// <summary>Prism's other <c>Subscribe</c> overloads all end up here.</summary>
        public override SubscriptionToken Subscribe(Action<TMessage> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<TMessage> filter)
        {
            MessageEventReceiver.Subscribed(typeof(TMessage));
            return base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
        }
    }
}
