using System;

namespace Legacy.RabbitMQ
{
    /// <summary>Sends Prism events to other applications over RabbitMQ.</summary>
    public interface IRabbitMQService : IDisposable
    {
        /// <summary>
        /// Queues <paramref name="payload"/> for publishing as <paramref name="eventType"/> to this application's exchange.
        /// Returns immediately; the outstanding queue sends it once a publisher connection is available.
        /// </summary>
        void Publish(Type eventType, object payload);
    }
}
