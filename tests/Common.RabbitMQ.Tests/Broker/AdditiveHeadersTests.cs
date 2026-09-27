using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// New headers are additive only (ADR 0001, section 3; delivery plan step 2): legacy receivers ignore them, the
    /// frozen headers can't be replaced, and without an observer nothing extra is sent.
    /// </summary>
    public class AdditiveHeadersTests
    {
        /// <summary>
        /// Added by RabbitMQ.Client itself when publisher confirmation tracking is on. Every build, including the
        /// baseline, has always sent it.
        /// </summary>
        private const string ClientSequenceNumber = "x-dotnet-pub-seq-no";

        [Fact]
        public async Task BothBuilds_ReceiveMessagesWithExtraHeaders()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver observer = new RecordingObserver(context =>
                {
                    context.Headers[WireHeaders.CorrelationId] = "corr-42";
                    context.Headers[WireHeaders.TraceParent] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
                    context.Headers["x-custom"] = "custom";

                    // Attempts to replace the frozen headers are ignored.
                    context.Headers[WireHeaders.MessageType] = "Something.Else";
                    context.Headers[WireHeaders.SourceId] = "someone-else";
                });

                CoreEndpoint sender = bus.AddCoreEndpoint("compat-server", observer);
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                CompatPayload payload = CompatPayloads.Golden();
                sender.PublishRemote(payload);

                CompatibilityMatrixTests.AssertPayload(payload, await current.Received.Next(TestBus.Timeout));
                CompatibilityMatrixTests.AssertPayload(payload, await baseline.Received.Next(TestBus.Timeout));

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(CoreEndpoint.WireName, message.RoutingKey);
                Assert.Equal(CoreEndpoint.WireName, message.Headers[WireHeaders.MessageType]);
                Assert.Equal(sender.InstanceId, message.Headers[WireHeaders.SourceId]);
                Assert.Equal("corr-42", message.Headers[WireHeaders.CorrelationId]);
                Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", message.Headers[WireHeaders.TraceParent]);
                Assert.Equal("custom", message.Headers["x-custom"]);
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
            }
        }

        [Theory]
        [InlineData(CompatBuild.Baseline)]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Core)]
        public async Task WithoutObserver_OnlyTheBaselineHeadersAreSent(CompatBuild build)
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint sender = bus.AddEndpoint(build, "compat-sender");
                await bus.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                sender.PublishRemote(CompatPayloads.Golden());

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                string[] expected = { WireHeaders.MessageType, WireHeaders.SourceId, ClientSequenceNumber };
                Assert.Equal(expected.OrderBy(h => h), message.Headers.Keys.OrderBy(h => h));
            }
        }
    }
}
