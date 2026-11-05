using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
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
