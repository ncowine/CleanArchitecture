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
        /// <summary>The other application's exchange.</summary>
        public string Exchange { get; set; }

        /// <summary>
        /// Routing keys to bind: patterns for a topic exchange (<c>orders.*.saved</c>, <c>AppA.Events.#</c>), exact keys for a
        /// direct exchange, anything for a fanout exchange. Empty means one binding per message type the bus handles, keyed
        /// by its wire name, as legacy applications do.
        /// </summary>
        public List<string> RoutingKeys { get; set; } = new List<string>();

        /// <summary>
        /// For a headers exchange: bind on these header values instead of routing keys. When set, <see cref="RoutingKeys"/>
        /// is ignored.
        /// </summary>
        public Dictionary<string, string> HeaderMatch { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Headers exchange only: true when every header in <see cref="HeaderMatch"/> must match (x-match all), false for any.</summary>
        public bool MatchAllHeaders { get; set; } = true;
    }
}
