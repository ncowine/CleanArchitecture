using System;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// The legacy adapter and the core over a real broker. The frozen baseline routes by event name, unlike the real
    /// legacy library, so it is no longer part of this: its headers and body are still checked in <see cref="WireFormatTests"/>.
    /// </summary>
    public class CompatibilityMatrixTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task PublishRemote_ReachesBothBuilds_AndIsNotEchoed()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Current, "compat-sender");
                ICompatEndpoint current = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                ICompatEndpoint core = bus.AddEndpoint(CompatBuild.Core, "compat-core");
                await bus.WaitUntilConnectedAsync();

                CompatPayload payload = CompatPayloads.Golden();
                sender.PublishRemote(payload);

                // Local subscribers are raised synchronously by PublishRemote.
                Assert.Equal(1, sender.Received.Count);

                AssertPayload(payload, await current.Received.NextAsync(TestBus.Timeout));
                AssertPayload(payload, await core.Received.NextAsync(TestBus.Timeout));

                // The sender's copy comes back from the broker alongside the others and must be dropped (source-id).
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, sender.Received.Count);
                Assert.Equal(1, current.Received.Count);
                Assert.Equal(1, core.Received.Count);
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
