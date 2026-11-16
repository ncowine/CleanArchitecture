using System.Collections.Generic;
using Messaging;

namespace Common.RabbitMQ
{
    internal static class RemoteEventSubscriptions
    {
        private static readonly object gate = new object();
        private static readonly HashSet<IRemoteEvent> subscribed = new HashSet<IRemoteEvent>();
        private static readonly List<RabbitMQServiceRouter> attachedRouters = new List<RabbitMQServiceRouter>();
        private static readonly List<(IRemoteEvent Event, SubscribeOptions Options)> boundKeys = new List<(IRemoteEvent Event, SubscribeOptions Options)>();

        public static void StartReceiving(IRemoteEvent remoteEvent)
        {
            lock (gate)
            {
                subscribed.Add(remoteEvent);

                foreach (RabbitMQServiceRouter router in attachedRouters)
                {
                    router.StartReceiving(remoteEvent);
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

                foreach (RabbitMQServiceRouter router in attachedRouters)
                {
                    router.StopReceiving(remoteEvent);
                }
            }
        }

        public static int AttachRouter(RabbitMQServiceRouter router)
        {
            lock (gate)
            {
                attachedRouters.Add(router);
                foreach ((IRemoteEvent remoteEvent, SubscribeOptions options) in boundKeys)
                {
                    router.BindRoutingKeyOnInit(remoteEvent, options);
                }

                int count = 0;
                foreach (IRemoteEvent remoteEvent in subscribed)
                {
                    if (router.StartReceiving(remoteEvent))
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
                List<RabbitMQServiceRouter> bound = new List<RabbitMQServiceRouter>();
                try
                {
                    foreach (RabbitMQServiceRouter router in attachedRouters)
                    {
                        router.BindRoutingKey(remoteEvent, options);
                        bound.Add(router);
                    }
                }
                catch
                {
                    foreach (RabbitMQServiceRouter router in bound)
                    {
                        router.UnbindRoutingKey(remoteEvent, options);
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
                foreach (RabbitMQServiceRouter router in attachedRouters)
                {
                    router.UnbindRoutingKey(remoteEvent, options);
                }
            }
        }

        public static void DetachRouter(RabbitMQServiceRouter router)
        {
            lock (gate)
            {
                attachedRouters.Remove(router);
            }
        }
    }
}
