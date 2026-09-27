namespace Messaging.Hosting
{
    /// <summary>
    /// Names to subscribe to in OpenTelemetry, for example <c>tracing.AddSource(MessagingTelemetry.Name)</c> and
    /// <c>metrics.AddMeter(MessagingTelemetry.Name)</c>.
    /// </summary>
    public static class MessagingTelemetry
    {
        /// <summary>The ActivitySource and Meter name.</summary>
        public const string Name = "Messaging";

        /// <summary>Counter: messages the broker confirmed, or failed to (with error.type).</summary>
        public const string SentMessages = "messaging.client.sent.messages";

        /// <summary>Counter: messages received and passed to handlers.</summary>
        public const string ConsumedMessages = "messaging.client.consumed.messages";

        /// <summary>Histogram, seconds: deserializing and handling one message.</summary>
        public const string ProcessDuration = "messaging.process.duration";
    }
}
