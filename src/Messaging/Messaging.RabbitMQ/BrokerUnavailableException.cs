using System;

namespace Messaging.RabbitMQ
{
    /// <summary>
    /// <see cref="RabbitMQBus.PublishConfirmedAsync"/> couldn't reach the broker: the bus isn't connected, or the
    /// connection dropped during the publish. Nothing was kept by the bus, so the caller should try again later.
    /// </summary>
    public sealed class BrokerUnavailableException : Exception
    {
        public BrokerUnavailableException(string message)
            : base(message)
        {
        }

        public BrokerUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
