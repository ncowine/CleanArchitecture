using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        public async Task ConfirmedPublisher_UsesTheWireNameAsKey_AndTheCallersMessageId()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => { }))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                await host.Services.GetRequiredService<IConfirmedMessagePublisher>()
                    .PublishConfirmedAsync(GoldenMessage(), "outbox-row-7", TestContext.Current.CancellationToken);

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal("outbox-row-7", message.MessageId);
                Assert.Equal("Compat.Events.CompatEvent", message.RoutingKey);
                Assert.Equal("Compat.Events.CompatEvent", message.Headers["event-type"]);
                Assert.Equal(0, host.Bus.OutstandingCount);
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_WithSeveralBuses_SendsToTheNamedBus_WithTheCallersKey()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.AddBus("Unreachable", options =>
            {
                options.HostName = "127.0.0.1";
                options.Port = 1;
                options.ExchangeName = "compat.unreachable";
                options.ClientName = "compat-unreachable";
            })))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                await host.Services.GetRequiredService<IConfirmedMessagePublisher>()
                    .PublishConfirmedAsync(GoldenMessage(), "outbox-row-8", new PublishOptions { Bus = host.Bus.BusName, RoutingKey = "call.key" }, TestContext.Current.CancellationToken);

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal("outbox-row-8", message.MessageId);
                Assert.Equal("call.key", message.RoutingKey);
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_WithTelemetry_SendsTheCurrentCorrelationId()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.AddTelemetry()))
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
        public async Task ConfirmedPublisher_DefaultKey_ReachesTheOriginalLibrary()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => { }))
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
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => { }, options => options.Port = 1))
            {
                await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
                    host.Services.GetRequiredService<IConfirmedMessagePublisher>().PublishConfirmedAsync(GoldenMessage(), null, TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_FailedPublish_EndsItsSpanWithAnError()
        {
            string messageId = "outbox-row-" + Guid.NewGuid().ToString("N");
            using (PublishSpans spans = new PublishSpans())
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.AddTelemetry(), options => options.Port = 1))
            {
                await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
                    host.Services.GetRequiredService<IConfirmedMessagePublisher>().PublishConfirmedAsync(GoldenMessage(), messageId, TestContext.Current.CancellationToken));

                // The outbox publishes again under a new context, so nothing else would ever end this span.
                Activity span = spans.Stopped(messageId);
                Assert.NotNull(span);
                Assert.Equal(ActivityStatusCode.Error, span.Status);
            }
        }

        [Fact]
        public async Task ConfirmedPublisher_CancelledPublish_EndsItsSpan()
        {
            string messageId = "outbox-row-" + Guid.NewGuid().ToString("N");
            using (PublishSpans spans = new PublishSpans())
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.AddTelemetry()))
            using (CancellationTokenSource shutdown = new CancellationTokenSource())
            {
                await host.WaitUntilConnected();
                shutdown.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    host.Services.GetRequiredService<IConfirmedMessagePublisher>().PublishConfirmedAsync(GoldenMessage(), messageId, shutdown.Token));

                Activity span = spans.Stopped(messageId);
                Assert.NotNull(span);
                Assert.Equal(ActivityStatusCode.Error, span.Status);
            }
        }

        private static CompatMessage GoldenMessage()
        {
            Compat.Events.CompatPayload golden = CompatPayloads.Golden();
            return new CompatMessage { Id = golden.Id, Name = golden.Name, UpdatedAt = golden.UpdatedAt };
        }

        /// <summary>Records the publish spans that end while it's alive.</summary>
        private sealed class PublishSpans : IDisposable
        {
            private readonly ConcurrentQueue<Activity> stopped = new ConcurrentQueue<Activity>();
            private readonly ActivityListener listener;

            public PublishSpans()
            {
                listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == MessagingTelemetry.Name,
                    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity => stopped.Enqueue(activity),
                };
                ActivitySource.AddActivityListener(listener);
            }

            public Activity Stopped(string messageId)
            {
                return stopped.FirstOrDefault(a => a.Kind == ActivityKind.Producer && (string)a.GetTagItem("messaging.message.id") == messageId);
            }

            public void Dispose()
            {
                listener.Dispose();
            }
        }
    }
}
