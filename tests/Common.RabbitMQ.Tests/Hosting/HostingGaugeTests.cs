using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// The connection gauge. Each test has a bus of its own name, because hosts in other tests
    /// report on the same meter at the same time.
    /// </summary>
    public class HostingGaugeTests
    {
        /// <summary>Nothing listens here: the bus never connects.</summary>
        private const int UnreachablePort = 1;

        [Fact]
        public async Task WhileDisconnected_BothConnectionsReadZero()
        {
            string busName = UniqueBusName();
            using (ServiceProvider provider = Build(busName, "127.0.0.1", UnreachablePort, null, "compat.unreachable"))
            {
                await StartAsync(provider);

                List<Reading> readings = Collect(busName);

                Assert.Equal(0, Single(readings, MessagingTelemetry.Connected, "consumer"));
                Assert.Equal(0, Single(readings, MessagingTelemetry.Connected, "publisher"));
                await StopAsync(provider);
            }
        }

        [Fact]
        public async Task WhileConnected_BothConnectionsReadOne()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                string busName = UniqueBusName();
                using (ServiceProvider provider = Build(busName, bus.Broker.HostName, bus.Broker.Port, bus.Broker, bus.ExchangeName))
                {
                    await StartAsync(provider);
                    await WaitUntil(() => Collect(busName).Count(r => r.Instrument == MessagingTelemetry.Connected && r.Value == 1) == 2);

                    List<Reading> readings = Collect(busName);

                    Assert.Equal(1, Single(readings, MessagingTelemetry.Connected, "consumer"));
                    Assert.Equal(1, Single(readings, MessagingTelemetry.Connected, "publisher"));
                    await StopAsync(provider);
                }
            }
        }

        [Fact]
        public async Task WithoutTelemetry_NothingIsReported()
        {
            string busName = UniqueBusName();
            using (ServiceProvider provider = Build(busName, "127.0.0.1", UnreachablePort, null, "compat.unreachable", telemetry: false))
            {
                await StartAsync(provider);

                Assert.Empty(Collect(busName));
                await StopAsync(provider);
            }
        }

        [Fact]
        public async Task AfterStopping_NothingIsReported()
        {
            string busName = UniqueBusName();
            using (ServiceProvider provider = Build(busName, "127.0.0.1", UnreachablePort, null, "compat.unreachable"))
            {
                await StartAsync(provider);
                await StopAsync(provider);

                Assert.Empty(Collect(busName));
            }
        }

        private static string UniqueBusName()
        {
            return "gauges-" + Guid.NewGuid().ToString("N");
        }

        private static ServiceProvider Build(string busName, string hostName, int port, BrokerSettings credentials, string exchangeName, bool telemetry = true)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddMessaging(messaging =>
            {
                messaging.AddBus(busName, options =>
                {
                    options.HostName = hostName;
                    options.Port = port;
                    options.VirtualHost = credentials?.VirtualHost ?? "/";
                    options.UserName = credentials?.UserName ?? "guest";
                    options.Password = credentials?.Password ?? "guest";
                    options.ExchangeName = exchangeName;
                    options.ClientName = "compat-gauges";
                    options.ReconnectDelay = TimeSpan.FromSeconds(1);
                });

                if (telemetry)
                {
                    messaging.AddTelemetry();
                }
            });

            return services.BuildServiceProvider();
        }

        private static async Task StartAsync(IServiceProvider provider)
        {
            foreach (IHostedService hostedService in provider.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(CancellationToken.None);
            }
        }

        private static async Task StopAsync(IServiceProvider provider)
        {
            foreach (IHostedService hostedService in provider.GetServices<IHostedService>())
            {
                await hostedService.StopAsync(CancellationToken.None);
            }
        }

        /// <summary>One collection of the messaging meter's observable instruments, keeping only <paramref name="busName"/>'s readings.</summary>
        private static List<Reading> Collect(string busName)
        {
            List<Reading> readings = new List<Reading>();
            using (MeterListener listener = new MeterListener())
            {
                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == MessagingTelemetry.Name)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };
                listener.SetMeasurementEventCallback<int>((instrument, value, tags, state) =>
                {
                    Dictionary<string, object> tagged = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, object> tag in tags)
                    {
                        tagged[tag.Key] = tag.Value;
                    }

                    if (Equals(tagged.GetValueOrDefault("messaging.bus.name"), busName))
                    {
                        readings.Add(new Reading(instrument.Name, tagged.GetValueOrDefault("messaging.connection.role") as string, value));
                    }
                });
                listener.Start();
                listener.RecordObservableInstruments();
            }

            return readings;
        }

        private static int Single(List<Reading> readings, string instrument, string role)
        {
            return Assert.Single(readings, r => r.Instrument == instrument && r.Role == role).Value;
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.Elapsed > TestBus.Timeout)
                {
                    throw new TimeoutException($"Not reached within {TestBus.Timeout.TotalSeconds}s.");
                }

                await Task.Delay(50);
            }
        }
    }
}
