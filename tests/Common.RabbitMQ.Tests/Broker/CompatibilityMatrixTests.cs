using System;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// Current build ↔ baseline build over a real broker, in both directions (ADR 0001, delivery plan step 0).
    /// Every sender reaches one receiver of each build, so the two cases cover all four directions.
    /// </summary>
    public class CompatibilityMatrixTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Theory]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Baseline)]
        public async Task PublishRemote_ReachesBothBuilds_AndIsNotEchoed(CompatBuild senderBuild)
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint sender = bus.AddEndpoint(senderBuild, "compat-sender");
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                CompatPayload payload = CompatPayloads.Golden();
                sender.PublishRemote(payload);

                // Local subscribers are raised synchronously by PublishRemote.
                Assert.Equal(1, sender.Received.Count);

                AssertPayload(payload, await current.Received.Next(TestBus.Timeout));
                AssertPayload(payload, await baseline.Received.Next(TestBus.Timeout));

                // The sender's copy comes back from the broker alongside the others and must be dropped (source-id).
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, sender.Received.Count);
                Assert.Equal(1, current.Received.Count);
                Assert.Equal(1, baseline.Received.Count);
            }
        }

        internal static void AssertPayload(CompatPayload expected, CompatPayload actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
            Assert.Equal(expected.UpdatedAt.Kind, actual.UpdatedAt.Kind);
        }
    }
}
