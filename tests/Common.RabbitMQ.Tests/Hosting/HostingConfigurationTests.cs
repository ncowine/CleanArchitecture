using System;
using System.Threading.Tasks;
using Compat.Events;
using Messaging;
using Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Configuration errors fail at startup. None of these need a broker.</summary>
    public class HostingConfigurationTests
    {
        /// <summary>Nothing listens here, so the bus never connects.</summary>
        private const int UnreachablePort = 1;

        [Fact]
        public async Task AddHandlers_OnUnknownBus_FailsAtStartup()
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                StartUnreachable(messaging => messaging.AddHandlers(typeof(CompatMessageHandler).Assembly, "Nowhere")));

            Assert.Contains("CompatMessageHandler receives from unknown bus 'Nowhere'", error.Message);
        }

        [Fact]
        public async Task PublishingAMessageWithoutWireName_Throws()
        {
            await using (TestHost host = await StartUnreachable(messaging => { }))
            {
                IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => publisher.Publish(new UnnamedMessage()));
                Assert.Contains("[Message(", error.Message);
            }
        }

        private static Task<TestHost> StartUnreachable(Action<MessagingBuilder> configure)
        {
            return TestHost.Start("127.0.0.1", UnreachablePort, null, "compat.unreachable", "compat-unreachable", configure);
        }
    }
}
