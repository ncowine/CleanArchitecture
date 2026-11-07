// Annotated for nullable-enabled callers: string? is optional, string is required. The project itself builds
// with nullable disabled, as legacy apps share it.
#nullable enable

namespace Messaging
{
    /// <summary>Overrides for one publish. Every property is optional: leave it null for the default.</summary>
    public sealed class PublishOptions
    {
        /// <summary>The bus to send on. Null for the default bus (<c>Messaging:DefaultBus</c>).</summary>
        public string? Bus { get; set; }

        /// <summary>
        /// Routes this message with this key instead of its wire name (ADR 0002, section 3). The wire name still travels in the
        /// event-type header; only subscribers whose bindings match the key receive it. Null for the exchange name.
        /// </summary>
        public string? RoutingKey { get; set; }
    }
}
