using System;
using Messaging;
using Prism.Events;

namespace Common.RabbitMQ
{
    public static class RemotePubSubEventExtensions
    {
        public static void PublishRemote<TPayload>(this RemotePubSubEvent<TPayload> remoteEvent, TPayload payload, PublishOptions options = null)
        {
            Send(remoteEvent, payload, options);
            remoteEvent.Publish(payload);
        }

        public static void PublishRemoteOnly<TPayload>(this RemotePubSubEvent<TPayload> remoteEvent, TPayload payload, PublishOptions options = null)
        {
            Send(remoteEvent, payload, options);
        }

        private static void Send<TPayload>(RemotePubSubEvent<TPayload> remoteEvent, TPayload payload, PublishOptions options)
        {
            if (remoteEvent == null)
            {
                throw new ArgumentNullException(nameof(remoteEvent));
            }

            IRabbitMQService service = Resolve(options?.Bus);
            if (string.IsNullOrEmpty(options?.RoutingKey))
            {
                service.Publish(remoteEvent.GetType(), payload);
                return;
            }

            if (!(service is IRoutingKeyPublisher routingPublisher))
            {
                throw new NotSupportedException($"{service.GetType().Name} does not support routing keys; it must implement {nameof(IRoutingKeyPublisher)}.");
            }

            routingPublisher.Publish(remoteEvent.GetType(), payload, options.RoutingKey);
        }

        private static IRabbitMQService Resolve(string bus)
        {
            IRabbitMQService service = RabbitMQServiceProvider.Resolve();
            if (string.IsNullOrEmpty(bus))
            {
                return service;
            }

            if (!(service is RabbitMQServiceRouter router))
            {
                throw new NotSupportedException($"Publishing to bus '{bus}' needs a {nameof(RabbitMQServiceRouter)}, but the application registered {service.GetType().Name}.");
            }

            return router.GetRequiredBus(bus);
        }
    }
}
