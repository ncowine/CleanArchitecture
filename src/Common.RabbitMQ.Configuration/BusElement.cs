using System;
using System.Configuration;
using System.Linq;

namespace Common.RabbitMQ.Configuration
{
    public class BusElement : ConfigurationElement
    {
        [ConfigurationProperty("name", IsRequired = true, IsKey = true)]
        public string Name => (string)this["name"];

        [ConfigurationProperty("hostName", DefaultValue = "localhost")]
        public string HostName => (string)this["hostName"];

        [ConfigurationProperty("port", DefaultValue = 5672)]
        public int Port => (int)this["port"];

        [ConfigurationProperty("virtualHost", DefaultValue = "/")]
        public string VirtualHost => (string)this["virtualHost"];

        [ConfigurationProperty("userName", DefaultValue = "guest")]
        public string UserName => (string)this["userName"];

        [ConfigurationProperty("password", DefaultValue = "guest")]
        public string Password => (string)this["password"];

        [ConfigurationProperty("exchangeName", IsRequired = true)]
        public string ExchangeName => (string)this["exchangeName"];

        [ConfigurationProperty("clientName", DefaultValue = "")]
        public string ClientName => (string)this["clientName"];

        [ConfigurationProperty("reconnectDelaySeconds", DefaultValue = 5)]
        public int ReconnectDelaySeconds => (int)this["reconnectDelaySeconds"];

        [ConfigurationProperty("outstandingPollIntervalMilliseconds", DefaultValue = 100)]
        public int OutstandingPollIntervalMilliseconds => (int)this["outstandingPollIntervalMilliseconds"];

        [ConfigurationProperty("prefetchCount", DefaultValue = 50)]
        public int PrefetchCount => (int)this["prefetchCount"];

        [ConfigurationProperty("heartbeatSeconds", DefaultValue = 30)]
        public int HeartbeatSeconds => (int)this["heartbeatSeconds"];

        /// <summary>The own exchange's type (ADR 0002): topic, direct, fanout or headers.</summary>
        [ConfigurationProperty("exchangeType", DefaultValue = "topic")]
        public string ExchangeType => (string)this["exchangeType"];

        /// <summary><c>&lt;subscriptions&gt;&lt;subscribe exchange="AppA" routingKeys="..." /&gt;&lt;/subscriptions&gt;</c></summary>
        [ConfigurationProperty("subscriptions")]
        public SubscriptionElementCollection Subscriptions => (SubscriptionElementCollection)this["subscriptions"];

        /// <summary><c>&lt;routes&gt;&lt;route event="Full.Name" routingKey="..." /&gt;&lt;/routes&gt;</c></summary>
        [ConfigurationProperty("routes")]
        public RouteElementCollection Routes => (RouteElementCollection)this["routes"];

        public RabbitMQConfig ToRabbitMQConfig()
        {
            return new RabbitMQConfig
            {
                BusName = Name,
                HostName = HostName,
                Port = Port,
                VirtualHost = VirtualHost,
                UserName = UserName,
                Password = Password,
                ExchangeName = ExchangeName,
                ClientName = ClientName,
                ReconnectDelaySeconds = ReconnectDelaySeconds,
                OutstandingPollIntervalMilliseconds = OutstandingPollIntervalMilliseconds,
                PrefetchCount = PrefetchCount,
                HeartbeatSeconds = HeartbeatSeconds,
                ExchangeType = ExchangeType,
                Subscriptions = Subscriptions.Cast<SubscriptionElement>().Select(s => s.ToSubscription()).ToList(),
                RoutingKeys = Routes.Cast<RouteElement>().ToDictionary(r => r.Event, r => r.RoutingKey, StringComparer.Ordinal),
            };
        }
    }
}
