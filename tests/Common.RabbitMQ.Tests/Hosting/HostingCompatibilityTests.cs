using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// A server built with Messaging.Hosting against the legacy builds over a real broker (ADR 0001, delivery plan
    /// step 3): plain-class contracts, System.Text.Json bodies, scoped async handlers, correlation and tracing.
    /// </summary>
    public class HostingCompatibilityTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task HostPublishes_BothLegacyBuildsReceive_WithTheGoldenBody()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-server", messaging => messaging.Route<CompatMessage>().To(CompatBus.Name).AddTelemetry()))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();
                    WireCapture capture = await bus.Capture();

                    CompatPayload payload = CompatPayloads.Golden();
                    await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(
                        new CompatMessage { Id = payload.Id, Name = payload.Name, UpdatedAt = payload.UpdatedAt },
                        TestContext.Current.CancellationToken);

                    CompatibilityMatrixTests.AssertPayload(payload, await current.Received.Next(TestBus.Timeout));
                    CompatibilityMatrixTests.AssertPayload(payload, await baseline.Received.Next(TestBus.Timeout));

                    CapturedMessage message = await capture.Next(TestBus.Timeout);
                    Assert.NotNull(message);
                    Assert.Equal("Compat.Events.CompatEvent", message.RoutingKey);
                    Assert.Equal("Compat.Events.CompatEvent", message.Headers[WireHeaders.MessageType]);
                    Assert.Equal(host.Bus.InstanceId, message.Headers[WireHeaders.SourceId]);
                    Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);

                    // Published outside any operation: the message starts its own conversation.
                    Assert.Equal(message.MessageId, message.Headers[WireHeaders.CorrelationId]);
                }
            }
        }

        [Fact]
        public async Task LegacyBuildsPublish_HandlerRunsInANewScopePerMessage()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-server", messaging => messaging.Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name)))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    CompatPayload payload = CompatPayloads.Golden();
                    current.PublishRemote(payload);
                    HandledMessage first = await host.Handled.Next(TestBus.Timeout);
                    baseline.PublishRemote(payload);
                    HandledMessage second = await host.Handled.Next(TestBus.Timeout);

                    foreach (HandledMessage handled in new[] { first, second })
                    {
                        Assert.NotNull(handled);
                        Assert.Equal(payload.Id, handled.Message.Id);
                        Assert.Equal(payload.Name, handled.Message.Name);
                        Assert.Equal(payload.UpdatedAt, handled.Message.UpdatedAt);
                        Assert.Equal(CompatBus.Name, handled.Context.BusName);

                        // Legacy senders set no correlation ID; the message ID starts the conversation.
                        Assert.Null(handled.Context.CorrelationId);
                        Assert.Equal(handled.Context.MessageId, handled.CorrelationId);
                    }

                    Assert.NotEqual(first.ScopeId, second.ScopeId);
                }
            }
        }

        [Fact]
        public async Task HandlerRepublishes_WithTheIncomingCorrelationId()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-server", messaging => messaging
                    .Route<CompatMessage>().To(CompatBus.Name)
                    .Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name).AddTelemetry()))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();
                    WireCapture capture = await bus.Capture();

                    CompatPayload payload = CompatPayloads.Golden();
                    payload.Name = CompatMessageHandler.Republish;
                    baseline.PublishRemote(payload);

                    CapturedMessage incoming = await capture.Next(TestBus.Timeout);
                    CapturedMessage reply = await capture.Next(TestBus.Timeout);
                    Assert.NotNull(incoming);
                    Assert.NotNull(reply);
                    Assert.Equal(baseline.InstanceId, incoming.Headers[WireHeaders.SourceId]);
                    Assert.Equal(host.Bus.InstanceId, reply.Headers[WireHeaders.SourceId]);
                    Assert.Equal(incoming.MessageId, reply.Headers[WireHeaders.CorrelationId]);

                    // The baseline first saw its own message locally (PublishRemote), then receives the reply.
                    // The host drops its own copy of the reply.
                    Assert.Equal(CompatMessageHandler.Republish, (await baseline.Received.Next(TestBus.Timeout))?.Name);
                    Assert.Equal(CompatMessageHandler.Republished, (await baseline.Received.Next(TestBus.Timeout))?.Name);
                    await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                    Assert.Equal(1, host.Handled.Count);
                }
            }
        }

        [Fact]
        public async Task TraceAndCorrelation_FlowFromHostToHost()
        {
            using (ActivitySource testSource = new ActivitySource("Common.RabbitMQ.Tests"))
            using (ActivityListener listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == MessagingTelemetry.Name || source.Name == testSource.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            })
            {
                ActivitySource.AddActivityListener(listener);

                await using (TestBus bus = await TestBus.Create())
                await using (TestHost sender = await TestHost.Start(bus, "compat-sender", messaging => messaging.Route<CompatMessage>().To(CompatBus.Name).AddTelemetry()))
                await using (TestHost receiver = await TestHost.Start(bus, "compat-receiver", messaging => messaging.Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name).AddTelemetry()))
                {
                    await sender.WaitUntilConnected();
                    await receiver.WaitUntilConnected();

                    ActivityTraceId traceId;
                    using (Activity request = testSource.StartActivity("request"))
                    using (CorrelationContext.Begin("corr-request"))
                    {
                        traceId = request.TraceId;
                        await sender.Services.GetRequiredService<IMessagePublisher>().PublishAsync(new CompatMessage { Id = 1, Name = "traced" }, TestContext.Current.CancellationToken);

                        // The publish span must not replace the caller's current activity.
                        Assert.Same(request, Activity.Current);
                    }

                    HandledMessage handled = await receiver.Handled.Next(TestBus.Timeout);
                    Assert.NotNull(handled);
                    Assert.Equal("corr-request", handled.Context.CorrelationId);
                    Assert.Equal("corr-request", handled.CorrelationId);
                    Assert.NotNull(handled.Activity);
                    Assert.Equal(ActivityKind.Consumer, handled.Activity.Kind);
                    Assert.Equal("process Compat.Events.CompatEvent", handled.Activity.OperationName);
                    Assert.Equal(traceId, handled.Activity.TraceId);
                }
            }
        }

        [Fact]
        public async Task FailingHandler_DoesNotStopTheBus()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-server", messaging => messaging.Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name)))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    CompatPayload failing = CompatPayloads.Golden();
                    failing.Name = CompatMessageHandler.Fail;
                    baseline.PublishRemote(failing);
                    baseline.PublishRemote(CompatPayloads.Golden());

                    HandledMessage handled = await host.Handled.Next(TestBus.Timeout);
                    Assert.NotNull(handled);
                    Assert.Equal(CompatPayloads.Golden().Name, handled.Message.Name);

                    await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                    Assert.Equal(1, host.Handled.Count);
                }
            }
        }

        [Fact]
        public async Task Health_IsHealthy_WhenConnected()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-server", messaging => { }))
            {
                await host.WaitUntilConnected();

                HealthReport report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

                Assert.Equal(HealthStatus.Healthy, report.Status);
            }
        }
    }
}
