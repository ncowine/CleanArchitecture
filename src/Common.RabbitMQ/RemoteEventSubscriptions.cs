using System.Collections.Generic;
using Messaging;

namespace Common.RabbitMQ
{
    internal static class RemoteEventSubscriptions
    {
        private static readonly object gate = new object();
        private static readonly HashSet<IRemoteEvent> subscribed = new HashSet<IRemoteEvent>();
        private static readonly List<RabbitMQService> running = new List<RabbitMQService>();
        private static readonly List<(IRemoteEvent Event, SubscribeOptions Options)> boundKeys = new List<(IRemoteEvent Event, SubscribeOptions Options)>();

        public static void StartReceiving(IRemoteEvent remoteEvent)
        {
            lock (gate)
            {
                subscribed.Add(remoteEvent);

                foreach (RabbitMQService service in running)
                {
                    service.StartReceiving(remoteEvent);
                }
            }
        }

        public static void StopReceivingIfUnused(IRemoteEvent remoteEvent)
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

        public static int AddRunningService(RabbitMQService service)
        {
            lock (gate)
            {
                running.Add(service);
                foreach ((IRemoteEvent remoteEvent, SubscribeOptions options) in boundKeys)
                {
                    service.BindOrLog(remoteEvent, options);
                }

                int count = 0;
                foreach (IRemoteEvent remoteEvent in subscribed)
                {
                    if (service.StartReceiving(remoteEvent))
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
                List<RabbitMQService> bound = new List<RabbitMQService>();
                try
                {
                    foreach (RabbitMQService service in running)
                    {
                        service.Bind(remoteEvent, options);
                        bound.Add(service);
                    }
                }
                catch
                {
                    foreach (RabbitMQService service in bound)
                    {
                        service.Unbind(remoteEvent, options);
                    }

                    throw;
                }

                boundKeys.Add((remoteEvent, options));
            }
        }

        public static void Unbind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            lock (gate)
            {
                int index = boundKeys.FindIndex(k => ReferenceEquals(k.Event, remoteEvent) && ReferenceEquals(k.Options, options));
                if (index < 0)
                {
                    return;
                }

                boundKeys.RemoveAt(index);
                foreach (RabbitMQService service in running)
                {
                    service.Unbind(remoteEvent, options);
                }
            }
        }

        public static void RemoveRunningService(RabbitMQService service)
        {
            lock (gate)
            {
                running.Remove(service);
            }
        }
    }
}
