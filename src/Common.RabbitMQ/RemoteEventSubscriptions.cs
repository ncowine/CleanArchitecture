using System.Collections.Generic;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Connects <see cref="RemotePubSubEvent{TPayload}"/> subscriptions to the services that receive them. Static because Prism
    /// creates the events and gives them no link back to the application; a view model can also subscribe before
    /// <see cref="RabbitMQService.Init"/>, so subscribed events are remembered until it runs.
    /// </summary>
    internal static class RemoteEventSubscriptions
    {
        private static readonly object gate = new object();
        private static readonly HashSet<IRemoteEvent> subscribed = new HashSet<IRemoteEvent>();
        private static readonly List<RabbitMQService> running = new List<RabbitMQService>();

        /// <summary>Asks every time, not just the first, so subscribing resumes an event stopped with <see cref="RabbitMQService.UnregisterQueue"/>.</summary>
        public static void Subscribed(IRemoteEvent remoteEvent)
        {
            lock (gate)
            {
                subscribed.Add(remoteEvent);

                foreach (RabbitMQService service in running)
                {
                    service.Receive(remoteEvent);
                }
            }
        }

        /// <summary>Checked under the lock, so a subscribe at the same moment either keeps the event received or starts it again.</summary>
        public static void StopIfUnused(IRemoteEvent remoteEvent)
        {
            lock (gate)
            {
                if (!subscribed.Contains(remoteEvent) || remoteEvent.HasSubscribers)
                {
                    return;
                }

                subscribed.Remove(remoteEvent);

                foreach (RabbitMQService service in running)
                {
                    service.StopReceiving(remoteEvent);
                }
            }
        }

        /// <summary>
        /// Gives <paramref name="service"/> the subscriptions made so far, and from now on the later ones and unsubscribes, all
        /// under the lock so none is missed. Returns how many were its own.
        /// </summary>
        public static int Join(RabbitMQService service)
        {
            lock (gate)
            {
                running.Add(service);

                int count = 0;
                foreach (IRemoteEvent remoteEvent in subscribed)
                {
                    if (service.Receive(remoteEvent))
                    {
                        count++;
                    }
                }

                return count;
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
