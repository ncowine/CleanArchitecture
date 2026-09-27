using System.Configuration;

namespace Common.RabbitMQ.Configuration
{
    /// <summary><c>&lt;route event="AppB.Events.CustomerChanged" routingKey="customers.eu.changed" /&gt;</c></summary>
    public class RouteElement : ConfigurationElement
    {
        /// <summary>The event's full type name.</summary>
        [ConfigurationProperty("event", IsRequired = true, IsKey = true)]
        public string Event => (string)this["event"];

        [ConfigurationProperty("routingKey", IsRequired = true)]
        public string RoutingKey => (string)this["routingKey"];
    }
}
