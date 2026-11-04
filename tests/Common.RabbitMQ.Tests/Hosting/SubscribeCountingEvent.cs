using System;
using Prism.Events;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Counts calls to the one <c>Subscribe</c> overload that <c>MessageEvent</c> overrides.</summary>
    public sealed class SubscribeCountingEvent : PubSubEvent<int>
    {
        public int Count { get; private set; }

        public override SubscriptionToken Subscribe(Action<int> action, ThreadOption threadOption, bool keepSubscriberReferenceAlive, Predicate<int> filter)
        {
            Count++;
            return base.Subscribe(action, threadOption, keepSubscriberReferenceAlive, filter);
        }
    }
}
