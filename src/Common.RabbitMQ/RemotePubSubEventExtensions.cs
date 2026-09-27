using System;
using Prism.Events;

namespace Common.RabbitMQ
{
    public static class RemotePubSubEventExtensions
    {
        /// <summary>
        /// Publishes the event to local subscribers and to every other application on the event's bus.
        /// The sender does not receive its own message back from RabbitMQ.
        /// </summary>
        /// <param name="rabbitMQService">
        /// Optional. When null the service is resolved through <see cref="RabbitMQServiceProvider"/>.
        /// </param>
        public static void PublishRemote<TPayload>(this RemotePubSubEvent<TPayload> remoteEvent, TPayload payload, IRabbitMQService rabbitMQService = null)
        {
            if (remoteEvent == null)
            {
                throw new ArgumentNullException(nameof(remoteEvent));
            }

            // Queue (and serialize) the remote message first so local subscribers can't change what gets sent.
            IRabbitMQService service = rabbitMQService ?? RabbitMQServiceProvider.Resolve();
            service.Publish(remoteEvent.GetType(), payload);

            remoteEvent.Publish(payload);
        }

        /// <summary>
        /// Publishes a plain <see cref="PubSubEvent{TPayload}"/> to local subscribers and to other applications, exactly as
        /// the <see cref="RemotePubSubEvent{TPayload}"/> overload does (ADR 0002, section 4). The event's assembly must be
        /// registered with <see cref="RemoteEventRegistry.Add"/>.
        /// </summary>
        public static void PublishRemote<TPayload>(this PubSubEvent<TPayload> pubSubEvent, TPayload payload, IRabbitMQService rabbitMQService = null)
        {
            if (pubSubEvent == null)
            {
                throw new ArgumentNullException(nameof(pubSubEvent));
            }

            IRabbitMQService service = rabbitMQService ?? RabbitMQServiceProvider.Resolve();
            service.Publish(pubSubEvent.GetType(), payload);

            pubSubEvent.Publish(payload);
        }

        /// <summary>
        /// As <c>PublishRemote</c>, routed with <paramref name="routingKey"/> instead of the event's full name (ADR 0002,
        /// section 3). A separate name, not an overload, so existing <c>PublishRemote(x, null)</c> calls stay unambiguous.
        /// Legacy subscribers bind full names, so they don't receive custom keys.
        /// </summary>
        public static void PublishRemoteTo<TPayload>(this PubSubEvent<TPayload> pubSubEvent, string routingKey, TPayload payload, IRabbitMQService rabbitMQService = null)
        {
            if (pubSubEvent == null)
            {
                throw new ArgumentNullException(nameof(pubSubEvent));
            }

            IRabbitMQService service = rabbitMQService ?? RabbitMQServiceProvider.Resolve();
            if (!(service is IRoutingKeyPublisher routingPublisher))
            {
                throw new NotSupportedException($"{service.GetType().Name} does not support routing keys; it must implement {nameof(IRoutingKeyPublisher)}.");
            }

            routingPublisher.Publish(pubSubEvent.GetType(), payload, routingKey);
            pubSubEvent.Publish(payload);
        }
    }
}
