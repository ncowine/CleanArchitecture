using System;
using Messaging;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Publishing a <see cref="RemotePubSubEvent{TPayload}"/> to other applications; plain <see cref="PubSubEvent{TPayload}"/>
    /// events are local. The service comes from <see cref="RabbitMQServiceProvider"/>. <see cref="PublishOptions"/> chooses the bus and routing key for one call; without it
    /// the event goes to the default bus (<see cref="RabbitMQServiceRouter.DefaultBus"/>), routed with the exchange name.
    /// Legacy subscribers bind the exchange name, so they don't receive custom keys. A bus that isn't configured throws.
    /// </summary>
    public static class RemotePubSubEventExtensions
    {
        /// <summary>
        /// Publishes the event to other applications, then to local subscribers.
        /// The sender does not receive its own message back from RabbitMQ.
        /// </summary>
        public static void PublishRemote<TPayload>(this RemotePubSubEvent<TPayload> remoteEvent, TPayload payload, PublishOptions options = null)
        {
            Send(remoteEvent, payload, options);
            remoteEvent.Publish(payload);
        }

        /// <summary>As <c>PublishRemote</c>, but only to other applications: local subscribers are not called.</summary>
        public static void PublishRemoteOnly<TPayload>(this RemotePubSubEvent<TPayload> remoteEvent, TPayload payload, PublishOptions options = null)
        {
            Send(remoteEvent, payload, options);
        }

        // Each public method queues (and serializes) the remote message BEFORE raising it locally, so local subscribers
        // can't change what gets sent.
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
