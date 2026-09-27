namespace Messaging
{
    /// <summary>Per-call settings for <see cref="IMessagePublisher.PublishAsync{TMessage}(TMessage, PublishOptions, System.Threading.CancellationToken)"/>.</summary>
    public sealed class PublishOptions
    {
        /// <summary>
        /// Overrides the route's routing key for this message (ADR 0002, section 3). The wire name still travels in the
        /// event-type header; only subscribers whose bindings match the key receive it.
        /// </summary>
        public string RoutingKey { get; set; }
    }
}
