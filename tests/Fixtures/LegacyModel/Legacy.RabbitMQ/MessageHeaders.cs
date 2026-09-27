namespace Legacy.RabbitMQ
{
    /// <summary>Assumptions A3: header names.</summary>
    public static class MessageHeaders
    {
        /// <summary>The event's full type name; the receiver resolves the event from it.</summary>
        public const string EventType = "event-type";

        /// <summary>The sending process, used to drop its own messages.</summary>
        public const string SourceId = "source-id";
    }
}
