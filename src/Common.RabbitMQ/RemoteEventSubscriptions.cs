using System.Collections.Generic;
using Messaging;

namespace Common.RabbitMQ
{
    internal static class RemoteEventSubscriptions
    {
        private static readonly object gate = new object();
        private static readonly HashSet<IRemoteEvent> subscribed = new HashSet<IRemoteEvent>();
        private static readonly List<RabbitMQService> running = new List<RabbitMQService>();
        private static readonly List<(IRemoteEvent Event, SubscribeOptions Options)> keyed = new List<(IRemoteEvent Event, SubscribeOptions Options)>();

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
