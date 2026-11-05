using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>Needs no broker: port 1 refuses every connection, and that failure is what the first test wants logged.</summary>
    public class LoggerTests
    {
        [Fact]
        public async Task RouterLogger_GetsTheAdaptersAndTheBusesMessages_AtTheirLevels()
        {
            RecordingLogger logger = new RecordingLogger();

            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), new RemoteEventRegistry()) { Logger = logger, DefaultBus = "Main" })
            {
                router.AddBus(new RabbitMQConfig { BusName = "Main", ExchangeName = "logger-test", ClientName = "logger-test", Port = 1 });
                router.Init();

                Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Initialized with 0 event(s)"));

                Stopwatch stopwatch = Stopwatch.StartNew();
                LogEntry failure;
                while ((failure = logger.Entries.FirstOrDefault(e => e.Message.Contains("could not connect"))) == null)
                {
                    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "No connection failure was logged.");
                    await Task.Delay(50, TestContext.Current.CancellationToken);
                }

                Assert.Equal(LogLevel.Warning, failure.Level);
                Assert.NotNull(failure.Exception);
                Assert.Contains("[Main]", failure.Message);
            }
        }

        [Fact]
        public void ServiceWithoutALogger_StillInitializes()
        {
            // Messages go to Trace, so an app that sets no logger needs no change.
            using (RabbitMQService service = new RabbitMQService(new EventAggregator(), new RemoteEventRegistry()))
            {
                service.Init(new RabbitMQConfig { BusName = "Main", ExchangeName = "logger-test", ClientName = "logger-test", Port = 1 });

                Assert.Null(service.Logger);
            }
        }
    }
}
