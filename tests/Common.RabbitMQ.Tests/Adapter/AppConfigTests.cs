using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using Common.RabbitMQ.Configuration;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>The App.config section: new ADR 0002 settings, and old configurations reading exactly as before.</summary>
    public class AppConfigTests
    {
        private const string Config = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <configSections>
    <section name=""rabbitMQ"" type=""Common.RabbitMQ.Configuration.RabbitMQConfigSection, Common.RabbitMQ.Configuration"" />
  </configSections>
  <rabbitMQ>
    <bus name=""Legacy"" exchangeName=""legacy.events"" clientName=""OldApp"" />
    <bus name=""AppB"" exchangeName=""AppB"" exchangeType=""direct"" clientName=""AppB"">
      <subscriptions>
        <subscribe exchange=""AppA"" />
        <subscribe exchange=""AppC"" routingKeys=""orders.*.saved, customers.#"" />
      </subscriptions>
      <routes>
        <route event=""AppB.Events.CustomerChanged"" routingKey=""customers.eu.changed"" />
      </routes>
    </bus>
  </rabbitMQ>
</configuration>";

        [Fact]
        public void OldStyleBus_ReadsAsBefore()
        {
            RabbitMQConfig legacy = Load().Single(c => c.BusName == "Legacy");

            Assert.Equal("legacy.events", legacy.ExchangeName);
            Assert.Equal("OldApp", legacy.ClientName);
            Assert.Equal("/", legacy.VirtualHost);
            Assert.Equal("topic", legacy.ExchangeType);
            Assert.Empty(legacy.Subscriptions);
            Assert.Empty(legacy.RoutingKeys);
        }

        [Fact]
        public void NewSettings_ExchangeTypeSubscriptionsAndRoutes()
        {
            RabbitMQConfig appB = Load().Single(c => c.BusName == "AppB");

            Assert.Equal("direct", appB.ExchangeType);
            Assert.Equal(2, appB.Subscriptions.Count);

            RabbitMQSubscription perEvent = appB.Subscriptions.Single(s => s.Exchange == "AppA");
            Assert.Empty(perEvent.RoutingKeys);

            RabbitMQSubscription patterns = appB.Subscriptions.Single(s => s.Exchange == "AppC");
            Assert.Equal(new[] { "orders.*.saved", "customers.#" }, patterns.RoutingKeys);

            Assert.Equal("customers.eu.changed", appB.RoutingKeys["AppB.Events.CustomerChanged"]);
        }

        private static List<RabbitMQConfig> Load()
        {
            string path = Path.Combine(Path.GetTempPath(), $"rabbitmq-config-{Guid.NewGuid():N}.config");
            File.WriteAllText(path, Config);
            try
            {
                ExeConfigurationFileMap map = new ExeConfigurationFileMap { ExeConfigFilename = path };
                System.Configuration.Configuration configuration = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);
                RabbitMQConfigSection section = (RabbitMQConfigSection)configuration.GetSection(RabbitMQConfigLoader.DefaultSectionName);

                return section.Buses.Cast<BusElement>().Select(b => b.ToRabbitMQConfig()).ToList();
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
