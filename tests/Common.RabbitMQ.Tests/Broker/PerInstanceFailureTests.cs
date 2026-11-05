using System;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>A handler that fails on a per-instance queue, the legacy behaviour (ADR 0001, section 3).</summary>
    public class PerInstanceFailureTests
    {
        /// <summary>How long to keep listening for a redelivery that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task FailingHandler_IsLogged_AndTheMessageIsAckedAndNotRedelivered()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-sender");
                CoreEndpoint receiver = bus.AddCoreEndpoint("compat-receiver");
                receiver.HandlerException = new InvalidOperationException("handler failed");
                await bus.WaitUntilConnected();

                sender.PublishRemote(CompatPayloads.Golden());

                LogEntry logged = await receiver.Log.WaitFor(e => e.Level == LogLevel.Error, TestBus.Timeout);
                Assert.NotNull(logged);
                Assert.Same(receiver.HandlerException, logged.Exception);
                Assert.Contains("acknowledged and dropped", logged.Message);

                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, receiver.Attempts);
            }
        }
    }
}
