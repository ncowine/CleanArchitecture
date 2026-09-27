using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// Legacy client queues: one per instance, named client.bus.instance, exclusive and removed with the client
    /// (ADR 0001, section 3). Exclusive queues are also what keeps this topology valid on RabbitMQ 4.x (section 6).
    /// </summary>
    public class QueueTopologyTests
    {
        private const ushort NotFound = 404;
        private const ushort ResourceLocked = 405;

        [Theory]
        [InlineData(CompatBuild.Current)]
        [InlineData(CompatBuild.Baseline)]
        [InlineData(CompatBuild.Core)]
        public async Task ConsumerQueue_IsExclusive_AndRemovedWithTheClient(CompatBuild build)
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint endpoint = bus.AddEndpoint(build, "Compat-Topology");
                await bus.WaitUntilConnected();

                // 404 here would mean the queue name changed; 200 that the queue is no longer exclusive.
                Assert.Equal(ResourceLocked, await bus.PassiveDeclare(endpoint.QueueName));

                endpoint.Dispose();

                Stopwatch stopwatch = Stopwatch.StartNew();
                ushort replyCode = await bus.PassiveDeclare(endpoint.QueueName);
                while (replyCode != NotFound && stopwatch.Elapsed < TestBus.Timeout)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                    replyCode = await bus.PassiveDeclare(endpoint.QueueName);
                }

                Assert.Equal(NotFound, replyCode);
            }
        }
    }
}
