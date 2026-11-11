using System;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// A server built with Messaging.Hosting against the legacy builds over a real broker (ADR 0001, delivery plan
    /// step 3): plain-class contracts, System.Text.Json bodies, and scoped async handlers.
    /// </summary>
    public class HostingCompatibilityTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task HostPublishes_BothLegacyBuildsReceive_WithTheGoldenBody()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");

                await using (TestHost host = await TestHost.StartAsync(bus, "compat-server", messaging => { }))
                {
                    await bus.WaitUntilConnectedAsync();
                    await host.WaitUntilConnectedAsync();
                    WireCapture capture = await bus.CaptureAsync();

                    CompatPayload payload = CompatPayloads.Golden();
                    host.Services.GetRequiredService<IMessagePublisher>().Publish(
                        new CompatMessage { Id = payload.Id, Name = payload.Name, UpdatedAt = payload.UpdatedAt });

                    CompatibilityMatrixTests.AssertPayload(payload, await current.Received.NextAsync(TestBus.Timeout));
                    CompatibilityMatrixTests.AssertPayload(payload, await legacy.Received.NextAsync(TestBus.Timeout));

                    CapturedMessage message = await capture.NextAsync(TestBus.Timeout);
                    Assert.NotNull(message);
                    Assert.Equal(bus.ExchangeName, message.RoutingKey);
                    Assert.Equal("Compat.Events.CompatEvent", message.Headers[WireHeaders.MessageType]);
                    Assert.Equal(host.Bus.InstanceId, message.Headers[WireHeaders.SourceId]);
                    Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
                }
            }
        }

        [Fact]
        public async Task LegacyBuildsPublish_HandlerRunsInANewScopePerMessage()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");

                await using (TestHost host = await TestHost.StartAsync(bus, "compat-server", messaging => messaging.AddHandlers(typeof(CompatMessageHandler).Assembly, CompatBus.Name)))
                {
                    await bus.WaitUntilConnectedAsync();
                    await host.WaitUntilConnectedAsync();

                    CompatPayload payload = CompatPayloads.Golden();
                    current.PublishRemote(payload);
                    HandledMessage first = await host.Handled.NextAsync(TestBus.Timeout);
                    legacy.PublishRemote(payload);
                    HandledMessage second = await host.Handled.NextAsync(TestBus.Timeout);

                    foreach (HandledMessage handled in new[] { first, second })
                    {
                        Assert.NotNull(handled);
                        Assert.Equal(payload.Id, handled.Message.Id);
                        Assert.Equal(payload.Name, handled.Message.Name);
                        Assert.Equal(payload.UpdatedAt, handled.Message.UpdatedAt);
                        Assert.Equal(CompatBus.Name, handled.Context.BusName);
                    }

                    Assert.NotEqual(first.ScopeId, second.ScopeId);
                }
            }
        }

        [Fact]
        public async Task AddHandlers_FindsTheAssemblysHandlers_OnTheOnlyBus()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");

                await using (TestHost host = await TestHost.StartAsync(bus, "compat-server", messaging => messaging.AddHandlers(typeof(CompatMessageHandler).Assembly)))
                {
                    await bus.WaitUntilConnectedAsync();
                    await host.WaitUntilConnectedAsync();

                    legacy.PublishRemote(CompatPayloads.Golden());

                    HandledMessage handled = await host.Handled.NextAsync(TestBus.Timeout);
                    Assert.NotNull(handled);
                    Assert.Equal(CompatPayloads.Golden().Id, handled.Message.Id);
                    Assert.Equal(CompatBus.Name, handled.Context.BusName);
                }
            }
        }

        [Fact]
        public async Task HandlerRepublishes_AndDropsItsOwnCopy()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");

                await using (TestHost host = await TestHost.StartAsync(bus, "compat-server", messaging => messaging
                    .AddHandlers(typeof(CompatMessageHandler).Assembly, CompatBus.Name)))
                {
                    await bus.WaitUntilConnectedAsync();
                    await host.WaitUntilConnectedAsync();
                    WireCapture capture = await bus.CaptureAsync();

                    CompatPayload payload = CompatPayloads.Golden();
                    payload.Name = CompatMessageHandler.Republish;
                    legacy.PublishRemote(payload);

                    CapturedMessage incoming = await capture.NextAsync(TestBus.Timeout);
                    CapturedMessage reply = await capture.NextAsync(TestBus.Timeout);
                    Assert.NotNull(incoming);
                    Assert.NotNull(reply);
                    Assert.Equal(legacy.InstanceId, incoming.Headers[WireHeaders.SourceId]);
                    Assert.Equal(host.Bus.InstanceId, reply.Headers[WireHeaders.SourceId]);

                    // The legacy app first saw its own message locally (PublishRemote), then receives the reply.
                    // The host drops its own copy of the reply.
                    Assert.Equal(CompatMessageHandler.Republish, (await legacy.Received.NextAsync(TestBus.Timeout))?.Name);
                    Assert.Equal(CompatMessageHandler.Republished, (await legacy.Received.NextAsync(TestBus.Timeout))?.Name);
                    await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                    Assert.Equal(1, host.Handled.Count);
                }
            }
        }

        [Fact]
        public async Task FailingHandler_DoesNotStopTheBus()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");

                await using (TestHost host = await TestHost.StartAsync(bus, "compat-server", messaging => messaging.AddHandlers(typeof(CompatMessageHandler).Assembly, CompatBus.Name)))
                {
                    await bus.WaitUntilConnectedAsync();
                    await host.WaitUntilConnectedAsync();

                    CompatPayload failing = CompatPayloads.Golden();
                    failing.Name = CompatMessageHandler.Fail;
                    legacy.PublishRemote(failing);
                    legacy.PublishRemote(CompatPayloads.Golden());

                    HandledMessage handled = await host.Handled.NextAsync(TestBus.Timeout);
                    Assert.NotNull(handled);
                    Assert.Equal(CompatPayloads.Golden().Name, handled.Message.Name);

                    await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                    Assert.Equal(1, host.Handled.Count);
                }
            }
        }
    }
}
