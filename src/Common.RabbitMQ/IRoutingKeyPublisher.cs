using System;

namespace Common.RabbitMQ
{
    public interface IRoutingKeyPublisher
    {
        void Publish(Type eventType, object payload, string routingKey);
    }
}
