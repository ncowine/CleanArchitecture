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
        /// sets none. Other keys are bound by subscribing with <see cref="Messaging.SubscribeOptions"/>.
        /// </summary>
        public string Exchange { get; set; }

        /// <summary>For a headers exchange: bind on these header values instead of the exchange's name.</summary>
        public Dictionary<string, string> HeaderMatch { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Headers exchange only: true when every header in <see cref="HeaderMatch"/> must match (x-match all), false for any.</summary>
        public bool MatchAllHeaders { get; set; } = true;
    }
}
