using System.Configuration;

namespace Common.RabbitMQ.Configuration
{
    /// <summary>
    /// App.config section:
    /// <code>
    /// &lt;rabbitMQ&gt;
    ///   &lt;bus name="AppB" exchangeName="AppB" exchangeType="topic" clientName="AppB" ...&gt;
    ///     &lt;subscriptions&gt;
    ///       &lt;subscribe exchange="AppA" /&gt;                                   &lt;!-- one binding per known event --&gt;
    ///       &lt;subscribe exchange="AppC" routingKeys="orders.*.saved" /&gt;      &lt;!-- patterns --&gt;
    ///     &lt;/subscriptions&gt;
    ///     &lt;routes&gt;
    ///       &lt;route event="AppB.Events.CustomerChanged" routingKey="customers.eu.changed" /&gt;
    ///     &lt;/routes&gt;
    ///   &lt;/bus&gt;
    /// &lt;/rabbitMQ&gt;
    /// </code>
    /// </summary>
    public class RabbitMQConfigSection : ConfigurationSection
    {
        [ConfigurationProperty("", IsDefaultCollection = true)]
        public BusElementCollection Buses => (BusElementCollection)base[""];
    }
}
