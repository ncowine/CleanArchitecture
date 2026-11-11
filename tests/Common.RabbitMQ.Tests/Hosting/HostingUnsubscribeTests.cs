using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Common.RabbitMQ.Tests.LegacyModel;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// The modern side: when the last subscriber goes, the type is dropped on arrival again, unless a registration still
    /// wants it. The queue's binding (one per exchange) never changes, so a recording sink shows what is received.
    /// </summary>
    public class HostingUnsubscribeTests
    {
        private const string App = "hosting-unsubscribe";
        private const string WireName = "Compat.Events.CompatEvent";

        private static readonly TimeSpan SettlePeriod = TimeSpan.FromMilliseconds(500);

        /// <summary>How long to wait for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task DisposingTheLastSubscription_StopsReceiving_AndSubscribingAgainResumes()
        {
            RecordingSink sink = new RecordingSink();
            await WithHostAsync(messaging => messaging.Services.AddSingleton<IMessageSink>(sink), async (topology, host) =>
            {
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                IDisposable first = subscriber.Subscribe<CompatMessage>(_ => { });
                IDisposable second = subscriber.Subscribe<CompatMessage>(_ => { });
                Assert.True(await IsReceivedAsync(topology, sink));

                first.Dispose();
                Assert.True(await IsReceivedAsync(topology, sink));

                second.Dispose();
                Assert.False(await IsReceivedAsync(topology, sink));

                subscriber.Subscribe<CompatMessage>(_ => { });
                Assert.True(await IsReceivedAsync(topology, sink));
            });
        }







        [Fact]
        public async Task SharedQueue_ASecondClassWithTheSameWireName_IsRefused_AfterTheFirstUnsubscribes()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");

                await using (TestHost host = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, topology.Exchange(App), App, messaging => { }, options => Share(options, service)))
                {
                    await host.WaitUntilConnectedAsync();
                    IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                    subscriber.Subscribe<CompatMessage>(_ => { }).Dispose();

                    InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => subscriber.Subscribe<Employees.Contracts.CompatMessageTwin>(_ => { }));
                    Assert.Contains(WireName, error.Message);
                }
            }
        }

        [Fact]
        public async Task SharedQueue_OneInstanceUnsubscribing_TheOthersStillReceive()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");
                Action<RabbitMQBusOptions> shared = options =>
                {
                    options.QueueMode = QueueMode.Shared;
                    options.SharedQueueName = service;
                };
                string exchange = topology.Exchange(App);
                Received<CompatMessage> atB = new Received<CompatMessage>();

                await using (TestHost b = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, messaging => { }, shared))
                {
                    b.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(atB.Add);
                    await b.WaitUntilConnectedAsync();

                    await using (TestHost a = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, messaging => { }, shared))
                    {
                        IDisposable subscription = a.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(_ => { });
                        await a.WaitUntilConnectedAsync();

                        subscription.Dispose();
                        await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                    }

                    // Only B consumes now.
                    await PublishGoldenAsync(topology);

                    Assert.Equal(42, (await atB.NextAsync(TestBus.Timeout))?.Id);
                }
            }
        }

        private static void Share(RabbitMQBusOptions options, string service)
        {
            options.QueueMode = QueueMode.Shared;
            options.SharedQueueName = service;
        }

        private static async Task WithHostAsync(Action<MessagingBuilder> configure, Func<LegacyTopology, TestHost, Task> test)
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            await using (TestHost host = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, topology.Exchange(App), App, configure))
            {
                await host.WaitUntilConnectedAsync();
                await test(topology, host);
            }
        }

        private static Task PublishGoldenAsync(LegacyTopology topology)
        {
            Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
            return topology.PublishRawAsync(App, topology.Exchange(App), headers, GoldenFile.Read("compat-payload.json"));
        }

        /// <summary>Publishes one message and says whether it reached the sink, i.e. wasn't dropped on arrival.</summary>
        private static async Task<bool> IsReceivedAsync(LegacyTopology topology, RecordingSink sink)
        {
            await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
            await PublishGoldenAsync(topology);
            return await sink.NextAsync(QuietPeriod) != null;
        }
    }
}
