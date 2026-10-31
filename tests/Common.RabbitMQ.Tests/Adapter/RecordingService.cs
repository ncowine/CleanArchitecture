using System;
using System.Collections.Generic;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>An <see cref="IRabbitMQService"/> that only records what it was asked to send.</summary>
    public sealed class RecordingService : IRabbitMQService, IRoutingKeyPublisher
    {
        /// <summary>The event type and routing key of each send; the routing key is null when none was given.</summary>
        public List<KeyValuePair<Type, string>> Sent { get; } = new List<KeyValuePair<Type, string>>();

        public void Publish(Type eventType, object payload)
        {
            Sent.Add(new KeyValuePair<Type, string>(eventType, null));
        }

        public void Publish(Type eventType, object payload, string routingKey)
        {
            Sent.Add(new KeyValuePair<Type, string>(eventType, routingKey));
        }

        public void Dispose()
        {
        }
    }
}
