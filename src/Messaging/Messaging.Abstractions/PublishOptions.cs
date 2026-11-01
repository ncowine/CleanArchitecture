namespace Messaging
{
    /// <summary>Per-call settings for <see cref="IMessagePublisher.Publish{TMessage}(TMessage, PublishOptions)"/>.</summary>
    public sealed class PublishOptions
    {
        /// <summary>
        /// Routes this message with this key instead of its wire name (ADR 0002, section 3). The wire name still travels in the
        /// event-type header; only subscribers whose bindings match the key receive it.
        /// </summary>
        public string RoutingKey { get; set; }
    }
}
