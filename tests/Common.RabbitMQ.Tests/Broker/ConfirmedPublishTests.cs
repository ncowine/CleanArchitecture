using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// <see cref="RabbitMQBus.PublishConfirmedAsync"/>, the server's publish for an outbox (ADR 0003): sent at once and
    /// confirmed by the broker, with nothing buffered. The wire format is the same as the buffered path's.
    /// </summary>
    public class ConfirmedPublishTests
    {
        [Fact]
        public async Task ConfirmedPublish_SendsTheFrozenWireFormat_WithTheCallersMessageId()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-core", null);
                await bus.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                await sender.Bus.PublishConfirmedAsync(CoreEndpoint.WireName, CompatPayloads.Golden(), null, "outbox-row-1", TestContext.Current.CancellationToken);

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal("outbox-row-1", message.MessageId);
                Assert.Equal(CoreEndpoint.WireName, message.RoutingKey);
                Assert.Equal(CoreEndpoint.WireName, message.Headers["event-type"]);
                Assert.Equal(sender.InstanceId, message.Headers["source-id"]);
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
                Assert.Equal(0, sender.Bus.OutstandingCount);
            }
        }

        [Fact]
        public async Task ConfirmedPublish_IsReceivedByTheOriginalLibrary()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-core", null);
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                await sender.Bus.PublishConfirmedAsync(CoreEndpoint.WireName, CompatPayloads.Golden(), null, null, TestContext.Current.CancellationToken);

                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await legacy.Received.Next(TestBus.Timeout));
            }
        }

        [Fact]
        public async Task ConfirmedPublish_WithoutAConnection_ThrowsBrokerUnavailable_AndKeepsNothing()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                // Nothing listens on port 1, so the bus never connects.
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-core", null, options => options.Port = 1);

                await Assert.ThrowsAsync<BrokerUnavailableException>(() =>
                    sender.Bus.PublishConfirmedAsync(CoreEndpoint.WireName, CompatPayloads.Golden(), null, null, TestContext.Current.CancellationToken));

                Assert.Equal(0, sender.Bus.OutstandingCount);
            }
        }

        [Fact]
        public async Task SharedQueue_WithAChangedSetting_ReportsWhatToDo()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                CoreEndpoint original = bus.AddCoreEndpoint(service, null, options =>
                {
                    options.QueueMode = QueueMode.Shared;
                    options.DeliveryLimit = 5;
                });
                await bus.WaitUntilConnected(original);
                original.Dispose();

                CoreEndpoint changed = bus.AddCoreEndpoint(service, null, options =>
                {
                    options.QueueMode = QueueMode.Shared;
                    options.DeliveryLimit = 6;
                });
                ConcurrentQueue<string> log = new ConcurrentQueue<string>();
                changed.Bus.Log += (sender, message) => log.Enqueue(message);

                // The connect loop retries every second, so the explanation is logged again after subscribing.
                Stopwatch stopwatch = Stopwatch.StartNew();
                string explanation;
                while ((explanation = log.FirstOrDefault(m => m.Contains("already exists with different settings"))) == null)
                {
                    Assert.True(stopwatch.Elapsed < TestBus.Timeout, "No explanation was logged. Log: " + string.Join(" | ", log));
                    await Task.Delay(50, TestContext.Current.CancellationToken);
                }

                Assert.Contains($"Queue '{changed.Bus.QueueName}'", explanation);
                Assert.Contains("DeliveryLimit", explanation);
                Assert.False(changed.Bus.IsConsumerConnected);
            }
        }
    }
}
