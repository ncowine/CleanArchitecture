using System;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>An <see cref="IRabbitMQService"/> written before routing keys existed, as an application might have.</summary>
    public sealed class PublishOnlyService : IRabbitMQService
    {
        public void Publish(Type eventType, object payload)
        {
        }

        public void Dispose()
        {
        }
    }
}
