// Annotated for nullable-enabled callers: string? is optional, string is required. The project itself builds
// with nullable disabled, as legacy apps share it.
#nullable enable

using System;
using System.Collections.Generic;

namespace Messaging.RabbitMQ
{
    /// <summary>
    /// Receiving from another application's exchange (ADR 0002, section 2). The bus binds its queue to
    /// <see cref="Exchange"/> but never declares it: the owner does. Until the owner has declared it, connecting fails
    /// and is retried.
    /// </summary>
    public sealed class SubscriptionOptions
    {
        /// <summary>
        /// The other application's exchange. It is bound with its name, the routing key its owner publishes with when it
        /// sets none. Other keys are bound by subscribing with <see cref="Messaging.SubscribeOptions"/>. Required.
        /// </summary>
        public string Exchange { get; set; } = null!;

        /// <summary>For a headers exchange: bind on these header values instead of the exchange's name. Optional.</summary>
        public Dictionary<string, string> HeaderMatch { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Headers exchange only: true (the default) when every header in <see cref="HeaderMatch"/> must match (x-match all), false for any.</summary>
        public bool MatchAllHeaders { get; set; } = true;
    }
}
