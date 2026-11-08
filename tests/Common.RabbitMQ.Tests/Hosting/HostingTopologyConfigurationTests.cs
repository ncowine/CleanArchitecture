using System.Collections.Generic;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>The ADR 0002 topology settings bind from configuration, as a server's appsettings.json provides them.</summary>
    public class HostingTopologyConfigurationTests
    {
        [Fact]
        public void TopologySettings_BindFromConfiguration()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Messaging:Buses:Server:ExchangeName"] = "Server",
                    ["Messaging:Buses:Server:ExchangeType"] = "direct",
                    ["Messaging:Buses:Server:BindOwnExchange"] = "false",
                    ["Messaging:Buses:Server:Subscriptions:0:Exchange"] = "AppA",
                    ["Messaging:Buses:Server:Subscriptions:1:Exchange"] = "Audit",
                    ["Messaging:Buses:Server:Subscriptions:2:Exchange"] = "AppB",
                })
                .Build();

            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddMessaging(messaging => messaging.AddBus("Server", configuration.GetSection("Messaging:Buses:Server")));

            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                RabbitMQBusOptions options = provider.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get("Server");

                Assert.Equal("Server", options.ExchangeName);
                Assert.Equal("direct", options.ExchangeType);
                Assert.False(options.BindOwnExchange);
                Assert.Equal(3, options.Subscriptions.Count);

                Assert.Equal("AppA", options.Subscriptions[0].Exchange);

                Assert.Equal("Audit", options.Subscriptions[1].Exchange);
                Assert.Equal("AppB", options.Subscriptions[2].Exchange);

                // The bus accepts the configuration.
                Assert.NotNull(provider.GetRequiredKeyedService<RabbitMQBus>("Server"));
            }
        }
    }
}
