namespace Messaging.RabbitMQ
{
    /// <summary>Header names on the wire (ADR 0001, section 3).</summary>
    public static class WireHeaders
    {
        /// <summary>
        /// Frozen. The message's wire name. Receivers fall back to the routing key when it is missing.
        /// </summary>
        public const string MessageType = "event-type";

        /// <summary>Frozen. The sending bus instance, used to drop the sender's own messages.</summary>
        public const string SourceId = "source-id";

        /// <summary>Optional, added by an observer. Read into <see cref="MessageContext.CorrelationId"/>.</summary>
        public const string CorrelationId = "correlation-id";

        /// <summary>Optional, added by an observer: W3C trace context.</summary>
        public const string TraceParent = "traceparent";

        /// <summary>Optional, added by an observer: W3C trace context.</summary>
        public const string TraceState = "tracestate";

        /// <summary>True for the headers the bus always writes itself and observers can't replace.</summary>
        public static bool IsFrozen(string name)
        {
            return name == MessageType || name == SourceId;
        }
    }
}
