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

        public string MessageId { get; }

        public bool Redelivered { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public string Exchange { get; }

        public string RoutingKey { get; }
    }
}
