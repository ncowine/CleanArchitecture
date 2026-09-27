using System;
using Prism.Events;

namespace Legacy.RabbitMQ
{
    public static class PubSubEventExtensions
    {
        /// <summary>
        /// Fact F1: works on any plain <see cref="PubSubEvent{TPayload}"/>. Queues the remote message, then raises the event
        /// for local subscribers (assumption A15). The sender does not receive its own message back (assumption A11).
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
    }
}
