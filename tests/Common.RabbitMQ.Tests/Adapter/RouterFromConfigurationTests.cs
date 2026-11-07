using System;
using System.IO;
using System.Text;
using AppA.Events;
using Microsoft.Extensions.Configuration;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary><see cref="RabbitMQServiceRouter.FromConfiguration"/>. No broker needed: the buses point at a port nothing listens on.</summary>
    public class RouterFromConfigurationTests
    {
        private const string Buses = @"
    ""Buses"": {
      ""First"": { ""HostName"": ""127.0.0.1"", ""Port"": 1, ""ExchangeName"": ""first"", ""ClientName"": ""router-tests"" },
      ""Second"": { ""HostName"": ""127.0.0.1"", ""Port"": 1, ""ExchangeName"": ""second"", ""ClientName"": ""router-tests"" }
    }";

        [Fact]
        public void AddsEveryBus_AndSendsEventsToTheDefault()
        {
            IConfiguration configuration = Load(@"""DefaultBus"": ""Second""," + Buses);

            using (RabbitMQServiceRouter router = RabbitMQServiceRouter.FromConfiguration(new EventAggregator(), configuration))
            {
                router.Init();

                router.Publish(typeof(OrderPlaced), new Order { Id = 1 });

                Assert.Equal(0, router.GetRequiredBus("First").OutstandingCount);
                Assert.Equal(1, router.GetRequiredBus("Second").OutstandingCount);
            }
        }

        [Fact]
        public void WithoutADefault_InitIsRefused()
        {
            using (RabbitMQServiceRouter router = RabbitMQServiceRouter.FromConfiguration(new EventAggregator(), Load(Buses)))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => router.Init());
                Assert.Contains("Messaging:DefaultBus", error.Message);
            }
        }

        [Fact]
        public void NoBuses_Throws()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                RabbitMQServiceRouter.FromConfiguration(new EventAggregator(), Load(@"""DefaultBus"": ""Main""")));

            Assert.Contains("Messaging:Buses", error.Message);
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
