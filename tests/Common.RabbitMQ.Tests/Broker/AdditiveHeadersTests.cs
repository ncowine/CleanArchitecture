using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// Every build sends only the frozen headers and ignores any others (ADR 0001, section 3). Messages already in a
    /// durable queue can still carry correlation-id and traceparent, which earlier versions of the server added.
    /// </summary>
    public class AdditiveHeadersTests
    {
        /// <summary>
        /// Added by RabbitMQ.Client itself when publisher confirmation tracking is on. Every build, including the
        /// baseline, has always sent it.
        /// </summary>
        private const string ClientSequenceNumber = "x-dotnet-pub-seq-no";

        [Fact]
        public async Task EveryBuild_ReceivesMessagesWithExtraHeaders()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint core = bus.AddEndpoint(CompatBuild.Core, "compat-core");
                await bus.WaitUntilConnected();

                Dictionary<string, object> headers = new Dictionary<string, object>
                {
                    [WireHeaders.MessageType] = CoreEndpoint.WireName,
                    ["correlation-id"] = "corr-42",
                    ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                    ["x-custom"] = "custom",
                };
                await bus.PublishRaw(headers, GoldenFile.Read("compat-payload.json"));

                CompatPayload payload = CompatPayloads.Golden();
                CompatibilityMatrixTests.AssertPayload(payload, await current.Received.Next(TestBus.Timeout));
                CompatibilityMatrixTests.AssertPayload(payload, await core.Received.Next(TestBus.Timeout));
            }
        }

        [Theory]
        [InlineData(CompatBuild.Baseline)]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Core)]
        public async Task OnlyTheBaselineHeadersAreSent(CompatBuild build)
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
