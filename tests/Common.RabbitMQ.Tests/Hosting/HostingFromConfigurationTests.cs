using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Compat.Events;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// <c>AddMessaging(configuration, ...)</c>, the only way an application adds buses. No broker needed: the buses point at
    /// a port nothing listens on.
    /// </summary>
    public class HostingFromConfigurationTests
    {
        private const string Buses = @"
    ""Buses"": {
      ""First"": { ""HostName"": ""127.0.0.1"", ""Port"": 1, ""ExchangeName"": ""first"", ""ClientName"": ""hosting-tests"" },
      ""Second"": { ""HostName"": ""127.0.0.1"", ""Port"": 1, ""ExchangeName"": ""second"", ""ClientName"": ""hosting-tests"" }
    }";

        [Fact]
        public async Task AddsEveryBus_AndPublishesToTheDefault()
        {
            IConfiguration configuration = Load(@"""DefaultBus"": ""Second""," + Buses);

            using (TestClient client = TestClient.Create(services => services.AddMessaging(configuration)))
            {
                await client.StartAsync(TestContext.Current.CancellationToken);
                RabbitMQBus first = client.Services.GetRequiredKeyedService<RabbitMQBus>("First");
                RabbitMQBus second = client.Services.GetRequiredKeyedService<RabbitMQBus>("Second");

                client.Publisher.Publish(new CompatMessage { Id = 1, Name = "one" });

                Assert.Equal((0, 1), (first.OutstandingCount, second.OutstandingCount));
                Assert.Equal("second", client.Services.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get("Second").ExchangeName);
            }
        }


        [Fact]
        public void WithoutADefault_Throws()
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                TestClient.Create(services => services.AddMessaging(Load(Buses))));

            Assert.Contains("Messaging:DefaultBus", error.Message);
        }

        [Fact]
        public void NoBuses_Throws()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                TestClient.Create(services => services.AddMessaging(Load(@"""DefaultBus"": ""Main"""))));

            Assert.Contains("Messaging:Buses", error.Message);
        }

        [Fact]
        public void ApplicationsCannotAddBusesByHand()
        {
            Assert.Null(typeof(MessagingBuilder).GetMethod("AddBus"));
            Assert.Null(typeof(MessagingBuilder).GetMethod("UseDefaultBus"));
        }

        private static IConfiguration Load(string messaging)
        {
            string json = @"{ ""Messaging"": { " + messaging + " } }";
            return new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build();
        }
    }
}
