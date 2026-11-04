using System;
using Messaging;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Publishing to other applications. The service comes from <see cref="RabbitMQServiceProvider"/>, and the event's
    /// assembly must be registered. <see cref="PublishOptions"/> chooses the bus and routing key for one call; without it
    /// the event goes to the default bus (<see cref="RabbitMQServiceRouter.DefaultBus"/>), routed with its full name or
    /// its entry in <see cref="RabbitMQConfig.RoutingKeys"/>. Legacy subscribers bind full names, so they don't receive
    /// custom keys. A bus that isn't configured throws.
    /// </summary>
    public static class RemotePubSubEventExtensions
    {
        /// <summary>
        /// Publishes the event to other applications, then to local subscribers.
        /// The sender does not receive its own message back from RabbitMQ.
        /// </summary>
        public static void PublishRemote<TPayload>(this PubSubEvent<TPayload> pubSubEvent, TPayload payload, PublishOptions options = null)
        {
            Send(pubSubEvent, payload, options);
            pubSubEvent.Publish(payload);
        }

        /// <summary>As <c>PublishRemote</c>, but only to other applications: local subscribers are not called.</summary>
        public static void PublishRemoteOnly<TPayload>(this PubSubEvent<TPayload> pubSubEvent, TPayload payload, PublishOptions options = null)
        {
            Send(pubSubEvent, payload, options);
        }

        // Each public method queues (and serializes) the remote message BEFORE raising it locally, so local subscribers
        // can't change what gets sent.
        private static void Send<TPayload>(PubSubEvent<TPayload> pubSubEvent, TPayload payload, PublishOptions options)
        {
            if (pubSubEvent == null)
            {
                throw new ArgumentNullException(nameof(pubSubEvent));
            }

            IRabbitMQService service = Resolve(options?.Bus);
            if (string.IsNullOrEmpty(options?.RoutingKey))
            {
                service.Publish(pubSubEvent.GetType(), payload);
                return;
            }

            if (!(service is IRoutingKeyPublisher routingPublisher))
            {
                throw new NotSupportedException($"{service.GetType().Name} does not support routing keys; it must implement {nameof(IRoutingKeyPublisher)}.");
            }

            routingPublisher.Publish(pubSubEvent.GetType(), payload, options.RoutingKey);
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
