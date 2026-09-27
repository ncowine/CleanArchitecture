using System;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary><see cref="IConfirmedMessagePublisher"/>, as the API's outbox uses it (ADR 0003).</summary>
    public class HostingConfirmedPublishTests
    {
        [Fact]
        public async Task ConfirmedPublisher_UsesTheRoutesKey_AndTheCallersMessageId()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>().WithRoutingKey(m => $"compat.{m.Id}")))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                await host.Services.GetRequiredService<IConfirmedMessagePublisher>()
                    .PublishConfirmedAsync(GoldenMessage(), "outbox-row-7", TestContext.Current.CancellationToken);

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal("outbox-row-7", message.MessageId);
                Assert.Equal($"compat.{CompatPayloads.Golden().Id}", message.RoutingKey);
                Assert.Equal("Compat.Events.CompatEvent", message.Headers["event-type"]);
                Assert.Equal(0, host.Bus.OutstandingCount);
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_WithTelemetry_SendsTheCurrentCorrelationId()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>().And().AddTelemetry()))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                using (CorrelationContext.Begin("corr-from-outbox"))
                {
                    await host.Services.GetRequiredService<IConfirmedMessagePublisher>()
                        .PublishConfirmedAsync(GoldenMessage(), "outbox-row-9", TestContext.Current.CancellationToken);
                }

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal("corr-from-outbox", message.Headers["correlation-id"]);
                Assert.Equal("outbox-row-9", message.MessageId);
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_MessageWithoutARoute_Throws()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>()))
            {
                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    host.Services.GetRequiredService<IConfirmedMessagePublisher>().PublishConfirmedAsync(new UnroutedMessage(), null, TestContext.Current.CancellationToken));

                Assert.Contains("has no route", error.Message);
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_DefaultKey_ReachesTheOriginalLibrary()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>()))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    await host.Services.GetRequiredService<IConfirmedMessagePublisher>()
                        .PublishConfirmedAsync(GoldenMessage(), null, TestContext.Current.CancellationToken);

                    CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await legacy.Received.Next(TestBus.Timeout));
                }
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_WithoutAConnection_ThrowsBrokerUnavailable()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>(), options => options.Port = 1))
            {
                await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
                    host.Services.GetRequiredService<IConfirmedMessagePublisher>().PublishConfirmedAsync(GoldenMessage(), null, TestContext.Current.CancellationToken));
            }
        }

        private static CompatMessage GoldenMessage()
        {
            Compat.Events.CompatPayload golden = CompatPayloads.Golden();
            return new CompatMessage { Id = golden.Id, Name = golden.Name, UpdatedAt = golden.UpdatedAt };
        }

        [Message("Compat.Tests.UnroutedMessage")]
        private sealed class UnroutedMessage
        {
        }
    }
}
