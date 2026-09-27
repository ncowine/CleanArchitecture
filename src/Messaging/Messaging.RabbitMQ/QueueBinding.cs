using System.Collections.Generic;

namespace Messaging.RabbitMQ
{
    /// <summary>One binding of the bus's queue to an exchange.</summary>
    internal sealed class QueueBinding
    {
        public QueueBinding(string exchange, string routingKey, IDictionary<string, object> arguments)
        {
            Exchange = exchange;
            RoutingKey = routingKey;
            Arguments = arguments;
        }

        public string Exchange { get; }

        public string RoutingKey { get; }

        /// <summary>Headers-exchange match arguments; null otherwise.</summary>
        public IDictionary<string, object> Arguments { get; }
    }
}
