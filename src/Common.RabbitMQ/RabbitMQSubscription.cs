using System.Collections.Generic;

namespace Common.RabbitMQ
{
    /// <summary>Receiving from another application's exchange (ADR 0002, section 2).</summary>
    public class RabbitMQSubscription
    {
        /// <summary>The other application's exchange. It is checked, never declared: its owner declares it.</summary>
        public string Exchange { get; set; }

        /// <summary>
        /// Routing keys or patterns to bind. Empty means one binding per event this application knows, keyed by the
        /// event's full name, which is what legacy applications do.
        /// </summary>
        public List<string> RoutingKeys { get; set; } = new List<string>();
    }
}
