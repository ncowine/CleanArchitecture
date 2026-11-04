using System.Collections.Generic;
using System.Linq;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Connects <see cref="RemotePubSubEvent{TPayload}"/> subscriptions to the services that bind them. Static because Prism
    /// creates the events and gives them no link back to the application; a view model can also subscribe before
    /// <see cref="RabbitMQService.Init"/>, so subscribed events are remembered until it runs.
    /// </summary>
    internal static class RemoteEventSubscriptions
    {
        private static readonly object gate = new object();
        private static readonly HashSet<object> subscribed = new HashSet<object>();
        private static readonly List<RabbitMQService> running = new List<RabbitMQService>();

        public static void Subscribed(object remoteEvent)
        {
            lock (gate)
            {
                if (!subscribed.Add(remoteEvent))
                {
                    return;
                }

                foreach (RabbitMQService service in running)
                {
                    service.Receive(remoteEvent);
                }
            }
        }

        /// <summary>Tells <paramref name="service"/> about later subscriptions and returns the ones made so far.</summary>
        public static IReadOnlyList<object> Join(RabbitMQService service)
        {
            lock (gate)
            {
                running.Add(service);
                return subscribed.ToList();
            }
        }

        public static void Leave(RabbitMQService service)
        {
            lock (gate)
            {
                running.Remove(service);
            }
        }
    }
}
