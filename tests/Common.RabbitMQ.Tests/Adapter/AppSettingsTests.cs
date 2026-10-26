using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// appsettings.json is the one configuration format. A legacy app binds <c>Messaging:Buses</c> onto
    /// <see cref="RabbitMQConfig"/>; a modern app binds the same section onto <see cref="RabbitMQBusOptions"/>.
    /// </summary>
    public class AppSettingsTests
    {
        private const string Json = @"{
  ""Messaging"": {
    ""Buses"": {
      ""Legacy"": { ""ExchangeName"": ""legacy.events"", ""ClientName"": ""OldApp"" },
      ""AppB"": {
        ""HostName"": ""broker"",
        ""UserName"": ""appb"",
        ""Password"": ""secret"",
        ""ExchangeName"": ""AppB"",
        ""ExchangeType"": ""direct"",
        ""ClientName"": ""AppB"",
        ""Subscriptions"": [
          { ""Exchange"": ""AppA"" },
          { ""Exchange"": ""AppC"", ""RoutingKeys"": [ ""orders.*.saved"", ""customers.#"" ] }
        ],
        ""RoutingKeys"": { ""AppB.Events.CustomerChanged"": ""customers.eu.changed"" }
      }
    }
  }
}";

        [Fact]
        public void MinimalBus_GetsTheLegacyDefaults()
        {
            RabbitMQConfig legacy = LoadLegacy().Single(c => c.BusName == "Legacy");

            Assert.Equal("legacy.events", legacy.ExchangeName);
            Assert.Equal("OldApp", legacy.ClientName);
            Assert.Equal("localhost", legacy.HostName);
            Assert.Equal("/", legacy.VirtualHost);
            Assert.Equal("topic", legacy.ExchangeType);
            Assert.Empty(legacy.Subscriptions);
            Assert.Empty(legacy.RoutingKeys);
        }

        [Fact]
        public void ExchangeTypeSubscriptionsAndRoutes()
        {
            RabbitMQConfig appB = LoadLegacy().Single(c => c.BusName == "AppB");

            Assert.Equal("direct", appB.ExchangeType);
            Assert.Empty(appB.Subscriptions.Single(s => s.Exchange == "AppA").RoutingKeys);
            Assert.Equal(new[] { "orders.*.saved", "customers.#" }, appB.Subscriptions.Single(s => s.Exchange == "AppC").RoutingKeys);
            Assert.Equal("customers.eu.changed", appB.RoutingKeys["AppB.Events.CustomerChanged"]);
        }

        [Fact]
        public void SameSection_BindsTheSameOntoBusOptions()
        {
            IConfigurationSection section = Load().GetSection("Messaging:Buses:AppB");
            RabbitMQConfig legacy = section.Get<RabbitMQConfig>();
            RabbitMQBusOptions modern = section.Get<RabbitMQBusOptions>();

            Assert.Equal(legacy.HostName, modern.HostName);
            Assert.Equal(legacy.UserName, modern.UserName);
            Assert.Equal(legacy.Password, modern.Password);
            Assert.Equal(legacy.ExchangeName, modern.ExchangeName);
            Assert.Equal(legacy.ExchangeType, modern.ExchangeType);
            Assert.Equal(legacy.ClientName, modern.ClientName);
            Assert.Equal(
                legacy.Subscriptions.Select(s => s.Exchange + ":" + string.Join(",", s.RoutingKeys)),
                modern.Subscriptions.Select(s => s.Exchange + ":" + string.Join(",", s.RoutingKeys)));
        }

        /// <summary>What a legacy app does at startup: one <see cref="RabbitMQConfig"/> per bus, named by its key.</summary>
        private static List<RabbitMQConfig> LoadLegacy()
        {
            List<RabbitMQConfig> configs = new List<RabbitMQConfig>();
            foreach (IConfigurationSection bus in Load().GetSection("Messaging:Buses").GetChildren())
            {
                RabbitMQConfig config = bus.Get<RabbitMQConfig>();
                config.BusName = bus.Key;
                configs.Add(config);
            }

            return configs;
        }

        private static IConfigurationRoot Load()
        {
            return new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Json)))
                .Build();
        }
    }
}
