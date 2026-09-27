using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    /// <summary>A subscription added in code, applied to its bus's options when the bus is created.</summary>
    internal sealed class PendingSubscription
    {
        public PendingSubscription(SubscriptionOptions subscription, string busName)
        {
            Subscription = subscription;
            BusName = busName;
        }

        public SubscriptionOptions Subscription { get; }

        /// <summary>Null means the application's only bus.</summary>
        public string BusName { get; }
    }
}
