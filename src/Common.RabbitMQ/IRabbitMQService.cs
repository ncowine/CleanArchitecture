using System;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Sends Prism events to other applications over RabbitMQ.
    /// </summary>
    public interface IRabbitMQService : IDisposable
    {
        /// <summary>
        /// Queues <paramref name="payload"/> for publishing as the remote event <paramref name="eventType"/>.
        /// Returns immediately; the message is sent by the outstanding queue once a publisher connection is available.
        /// </summary>
        void Publish(Type eventType, object payload);
    }
}
