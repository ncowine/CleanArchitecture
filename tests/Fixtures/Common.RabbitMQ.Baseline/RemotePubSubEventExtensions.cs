using System;

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
    }
}
