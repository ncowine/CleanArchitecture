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
    }
}
