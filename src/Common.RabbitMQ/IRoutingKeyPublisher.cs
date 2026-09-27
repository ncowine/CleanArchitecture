using System;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Publishing with a chosen routing key (ADR 0002, section 3). A separate interface so <see cref="IRabbitMQService"/>
    /// stays unchanged for code that implements it. <see cref="RabbitMQService"/> and <see cref="RabbitMQServiceRouter"/>
    /// implement both.
    /// </summary>
    public interface IRoutingKeyPublisher
    {
        /// <summary>As <see cref="IRabbitMQService.Publish"/>, routed with <paramref name="routingKey"/> instead of the event's full name.</summary>
        void Publish(Type eventType, object payload, string routingKey);
    }
}
