using System.Collections.Generic;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// What each build actually puts on and accepts from the wire, checked against the golden file
    /// (ADR 0001, section 3). New headers may be added; the ones checked here may not change.
    /// </summary>
    public class WireFormatTests
    {
        private const string WireName = "Compat.Events.CompatEvent";
        private const string GoldenBody = "compat-payload.json";

        [Theory]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Baseline)]
        [InlineData(CompatBuild.Core)]
        public async Task Sends_FrozenRoutingKeyHeadersAndBody(CompatBuild build)
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint sender = bus.AddEndpoint(build, "compat-sender");
                await bus.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                sender.PublishRemote(CompatPayloads.Golden());

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(bus.ExchangeName, message.Exchange);
                Assert.Equal(WireName, message.RoutingKey);
                Assert.Equal(WireName, message.Headers[MessageHeaders.EventType]);
                Assert.Equal(sender.InstanceId, message.Headers[MessageHeaders.SourceId]);
                Assert.Equal("application/json", message.ContentType);
                Assert.Equal("compat-sender", message.AppId);
                Assert.False(string.IsNullOrEmpty(message.MessageId));
                Assert.Equal(GoldenFile.Read(GoldenBody), message.Body);
            }
        }

        [Theory]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Baseline)]
        [InlineData(CompatBuild.Core)]
        public async Task Receives_GoldenMessageFromAnotherApplication(CompatBuild build)
        {
            Dictionary<string, object> headers = new Dictionary<string, object>
            {
                [MessageHeaders.EventType] = WireName,
                [MessageHeaders.SourceId] = "another-application",
            };

            await ReceivesGoldenMessage(build, headers);
        }

        [Theory]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Baseline)]
        [InlineData(CompatBuild.Core)]
        public async Task Receives_ByRoutingKey_WhenHeadersAreMissing(CompatBuild build)
        {
            await ReceivesGoldenMessage(build, null);
        }

        private static async Task ReceivesGoldenMessage(CompatBuild build, IDictionary<string, object> headers)
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint receiver = bus.AddEndpoint(build, "compat-receiver");
                await bus.WaitUntilConnected();

                await bus.PublishRaw(WireName, headers, GoldenFile.Read(GoldenBody));

                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await receiver.Received.Next(TestBus.Timeout));
            }
        }
    }
}
