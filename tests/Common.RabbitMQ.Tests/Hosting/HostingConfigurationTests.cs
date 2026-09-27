using System;
using System.Threading.Tasks;
using Compat.Events;
using Messaging;
using Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Configuration errors fail at startup, and health reflects the connections. None of these need a broker.</summary>
    public class HostingConfigurationTests
    {
        /// <summary>Nothing listens here, so the bus never connects.</summary>
        private const int UnreachablePort = 1;

        [Fact]
        public async Task RouteToUnknownBus_FailsAtStartup()
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                StartUnreachable(messaging => messaging.Route<CompatMessage>().To("Nowhere")));

            Assert.Contains("unknown bus 'Nowhere'", error.Message);
        }

        [Fact]
        public async Task HandlerOnUnknownBus_FailsAtStartup()
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                StartUnreachable(messaging => messaging.Handle<CompatMessage, CompatMessageHandler>().From("Nowhere")));

            Assert.Contains("unknown bus 'Nowhere'", error.Message);
        }

        [Fact]
        public async Task MessageWithoutWireName_FailsAtStartup()
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                StartUnreachable(messaging => messaging.Route<UnnamedMessage>().To(CompatBus.Name)));

            Assert.Contains("[Message(", error.Message);
        }

        [Fact]
        public async Task PublishingAnUnroutedMessage_Throws()
        {
            await using (TestHost host = await StartUnreachable(messaging => { }))
            {
                IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

                await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new CompatMessage(), TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task Health_IsUnhealthy_WhileTheBusIsDisconnected()
        {
            await using (TestHost host = await StartUnreachable(messaging => { }))
            {
                HealthReport report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

                Assert.Equal(HealthStatus.Unhealthy, report.Status);
                Assert.Equal("disconnected", report.Entries["messaging"].Data[$"{CompatBus.Name}.consumer"]);
            }
        }

        private static Task<TestHost> StartUnreachable(Action<MessagingBuilder> configure)
        {
            return TestHost.Start("127.0.0.1", UnreachablePort, null, "compat.unreachable", "compat-unreachable", configure);
        }
    }
}
