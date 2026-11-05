using System.Collections.Generic;

namespace Messaging
{
    public sealed class MessageContext
    {
        public MessageContext(string wireName, string busName, string messageId, bool redelivered, IReadOnlyDictionary<string, string> headers, string exchange, string routingKey)
        {
            WireName = wireName;
            BusName = busName;
            MessageId = messageId;
            Redelivered = redelivered;
            Headers = headers;
            Exchange = exchange;
            RoutingKey = routingKey;
        }

        public string WireName { get; }

        public string BusName { get; }

        /// <summary>Null when the sender did not set one.</summary>
        public string MessageId { get; }

        /// <summary>True when the broker delivered this message before and it was not acknowledged.</summary>
        public bool Redelivered { get; }

        /// <summary>Header values decoded as UTF-8 strings.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>The exchange the message was published to: which application sent it.</summary>
        public string Exchange { get; }

        /// <summary>The routing key it was published with. Not its identity: see <see cref="WireName"/>.</summary>
        public string RoutingKey { get; }
    }
}
