using System.Collections.Generic;
using Messaging;

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
        private static readonly List<(IRemoteEvent Event, SubscribeOptions Options)> keyed = new List<(IRemoteEvent Event, SubscribeOptions Options)>();

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
                foreach ((IRemoteEvent remoteEvent, SubscribeOptions options) in keyed)
                {
                    service.TryBind(remoteEvent, options);
                }

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

        /// <summary>Binds a subscription's routing key, now or when the service starts. Undone by <see cref="Unbind"/>.</summary>
        public static void Bind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            lock (gate)
            {
                foreach (RabbitMQService service in running)
                {
                    service.Bind(remoteEvent, options);
                }

                keyed.Add((remoteEvent, options));
            }
        }

        public static void Unbind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            lock (gate)
            {
                int index = keyed.FindIndex(k => ReferenceEquals(k.Event, remoteEvent) && ReferenceEquals(k.Options, options));
                if (index < 0)
                {
                    return;
                }

                keyed.RemoveAt(index);
                foreach (RabbitMQService service in running)
                {
                    service.Unbind(remoteEvent, options);
                }
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
