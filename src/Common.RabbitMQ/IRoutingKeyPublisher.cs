using System;

namespace Common.RabbitMQ
{
    internal interface IRoutingKeyPublisher
    {
        void Publish(Type eventType, object payload, string routingKey);
    }
}
