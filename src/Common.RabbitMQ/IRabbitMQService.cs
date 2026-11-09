using System;

namespace Common.RabbitMQ
{
    public interface IRabbitMQService : IDisposable
    {
        void Publish(Type eventType, object payload);
    }
}
