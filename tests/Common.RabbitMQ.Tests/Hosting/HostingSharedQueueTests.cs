using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Compat.Events;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Shared queues through Messaging.Hosting, as the API runs them (delivery plan step 3b).</summary>
    public class HostingSharedQueueTests
    {
        [Fact]
        public void SharedQueueSettings_BindFromConfiguration()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Messaging:Buses:Legacy:ExchangeName"] = "legacy.events",
                    ["Messaging:Buses:Legacy:ClientName"] = "WebApi",
                    ["Messaging:Buses:Legacy:QueueMode"] = "Shared",
                    ["Messaging:Buses:Legacy:MaxAttempts"] = "4",
                    ["Messaging:Buses:Legacy:RetryDelay"] = "00:00:02",
                    ["Messaging:Buses:Legacy:DeliveryLimit"] = "7",
                })
                .Build();

            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddMessaging(messaging => messaging.AddBus("Legacy", configuration.GetSection("Messaging:Buses:Legacy")));

            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                RabbitMQBusOptions options = provider.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get("Legacy");
                Assert.Equal("Legacy", options.BusName);
                Assert.Equal(QueueMode.Shared, options.QueueMode);
                Assert.Equal(4, options.MaxAttempts);
                Assert.Equal(TimeSpan.FromSeconds(2), options.RetryDelay);
                Assert.Equal(7, options.DeliveryLimit);

                RabbitMQBus bus = provider.GetRequiredKeyedService<RabbitMQBus>("Legacy");
                Assert.Equal("webapi.legacy", bus.QueueName);
                Assert.Equal("webapi.legacy.dead-letter", bus.DeadLetterQueueName);
            }
        }

        [Fact]
        public async Task TwoHostInstances_ShareTheWork_EachMessageOnce()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                Action<RabbitMQBusOptions> shared = options =>
                {
                    options.QueueMode = QueueMode.Shared;
                    options.PrefetchCount = 1;
                };

                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await using (TestHost first = await TestHost.Start(bus, service, messaging => messaging.Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name), shared))
                await using (TestHost second = await TestHost.Start(bus, service, messaging => messaging.Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name), shared))
                {
                    bus.DeleteOnDispose(first.Bus.QueueName, first.Bus.DeadLetterQueueName);
                    await bus.WaitUntilConnected();
                    await first.WaitUntilConnected();
                    await second.WaitUntilConnected();

                    const int count = 20;
                    for (int id = 1; id <= count; id++)
                    {
                        sender.PublishRemote(new CompatPayload { Id = id, Name = "work", UpdatedAt = DateTime.UtcNow });
                    }

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    while (first.Handled.Count + second.Handled.Count < count && stopwatch.Elapsed < TestBus.Timeout)
                    {
                        await Task.Delay(50, TestContext.Current.CancellationToken);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

                    List<int> ids = (await Drain(first.Handled)).Concat(await Drain(second.Handled)).Select(h => h.Message.Id).ToList();
                    Assert.Equal(Enumerable.Range(1, count), ids.OrderBy(id => id));
                    Assert.True(first.Handled.Count > 0 && second.Handled.Count > 0, $"Work was not shared: {first.Handled.Count} and {second.Handled.Count}.");
                }
            }
        }

        private static async Task<List<HandledMessage>> Drain(HandledMessages handled)
        {
            List<HandledMessage> messages = new List<HandledMessage>();
            for (HandledMessage message = await handled.Next(TimeSpan.Zero); message != null; message = await handled.Next(TimeSpan.Zero))
            {
                messages.Add(message);
            }

            return messages;
        }
    }
}
