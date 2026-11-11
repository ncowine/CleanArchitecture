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
        ""UseTls"": true,
        ""TlsServerName"": ""broker.example.com"",
        ""ExchangeName"": ""AppB"",
        ""ExchangeType"": ""direct"",
        ""ClientName"": ""AppB"",
        ""Subscriptions"": [
          { ""Exchange"": ""AppA"" },
          { ""Exchange"": ""AppC"" }
        ]
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
            Assert.False(legacy.UseTls);
            Assert.Empty(legacy.Subscriptions);
        }

        [Fact]
        public void ExchangeTypeSubscriptionsAndRoutes()
        {
            RabbitMQConfig appB = LoadLegacy().Single(c => c.BusName == "AppB");

            Assert.Equal("direct", appB.ExchangeType);
            Assert.Equal(new[] { "AppA", "AppC" }, appB.Subscriptions.Select(s => s.Exchange));
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
            Assert.True(modern.UseTls);
            Assert.Equal(legacy.UseTls, modern.UseTls);
            Assert.Equal("broker.example.com", modern.TlsServerName);
            Assert.Equal(legacy.TlsServerName, modern.TlsServerName);
            Assert.Equal(legacy.ExchangeName, modern.ExchangeName);
            Assert.Equal(legacy.ExchangeType, modern.ExchangeType);
            Assert.Equal(legacy.ClientName, modern.ClientName);
            Assert.Equal(
                legacy.Subscriptions.Select(s => s.Exchange),
                modern.Subscriptions.Select(s => s.Exchange));
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
