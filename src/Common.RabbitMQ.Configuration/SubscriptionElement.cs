using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Common.RabbitMQ.Configuration
{
    /// <summary><c>&lt;subscribe exchange="AppA" routingKeys="AppA.Events.#, orders.*.saved" /&gt;</c></summary>
    public class SubscriptionElement : ConfigurationElement
    {
        [ConfigurationProperty("exchange", IsRequired = true, IsKey = true)]
        public string Exchange => (string)this["exchange"];

        /// <summary>Comma-separated. Empty: one binding per known event, keyed by its full name.</summary>
        [ConfigurationProperty("routingKeys", DefaultValue = "")]
        public string RoutingKeys => (string)this["routingKeys"];

        public RabbitMQSubscription ToSubscription()
        {
            List<string> keys = RoutingKeys
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToList();

            return new RabbitMQSubscription { Exchange = Exchange, RoutingKeys = keys };
        }
    }
}
