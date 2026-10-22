using System;
using System.Collections.Generic;

namespace Messaging
{
    /// <summary>A message on its way out, as <see cref="IMessagingObserver"/> sees it.</summary>
    public sealed class PublishContext
    {
        public PublishContext(string wireName, string busName, string messageId, Type messageType, string routingKey)
            : this(wireName, busName, messageId, messageType, routingKey, retriedOnFailure: true)
        {
        }

        /// <param name="retriedOnFailure">See <see cref="RetriedOnFailure"/>.</param>
        public PublishContext(string wireName, string busName, string messageId, Type messageType, string routingKey, bool retriedOnFailure)
        {
            WireName = wireName;
            BusName = busName;
            MessageId = messageId;
            MessageType = messageType;
            RoutingKey = routingKey;
            RetriedOnFailure = retriedOnFailure;
        }

        public string WireName { get; }

        public string BusName { get; }

        /// <summary>Sent as the message-id property.</summary>
        public string MessageId { get; }

        /// <summary>The message's runtime type, or null for a null message.</summary>
        public Type MessageType { get; }

        /// <summary>Where the message is routed: the wire name unless the publisher chose another key.</summary>
        public string RoutingKey { get; }

        /// <summary>
        /// True when the bus keeps the message after <see cref="IMessagingObserver.OnPublishFailed"/> and retries it under
        /// this same context, as the buffered path does. False for a confirmed publish: a failure is the last call for this
        /// context, and the caller decides whether to publish again (as a new context).
        /// </summary>
        public bool RetriedOnFailure { get; }

        /// <summary>
        /// Extra headers to send, added in <see cref="IMessagingObserver.OnPublishing"/>. Additive only: receivers of
        /// every build ignore headers they don't know, and the frozen headers can't be overwritten.
        /// </summary>
        public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>State an observer keeps between its callbacks for this message.</summary>
        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
    }
}
