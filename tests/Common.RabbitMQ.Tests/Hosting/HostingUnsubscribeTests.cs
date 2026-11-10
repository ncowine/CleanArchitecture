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
            await WithHost(messaging => messaging.Services.AddSingleton<IMessageSink>(sink), async (topology, host) =>
            {
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                IDisposable first = subscriber.Subscribe<CompatMessage>(_ => { });
                IDisposable second = subscriber.Subscribe<CompatMessage>(_ => { });
                Assert.True(await IsReceived(topology, sink));

                first.Dispose();
                Assert.True(await IsReceived(topology, sink));

                second.Dispose();
                Assert.False(await IsReceived(topology, sink));

                subscriber.Subscribe<CompatMessage>(_ => { });
                Assert.True(await IsReceived(topology, sink));
            });
        }







        [Fact]
        public async Task SharedQueue_ASecondClassWithTheSameWireName_IsRefused_AfterTheFirstUnsubscribes()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");

                await using (TestHost host = await TestHost.Start(topology.Broker.HostName, topology.Broker.Port, topology.Broker, topology.Exchange(App), App, messaging => { }, options => Share(options, service)))
                {
                    await host.WaitUntilConnected();
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
            await using (LegacyTopology topology = await LegacyTopology.Create())
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

                await using (TestHost b = await TestHost.Start(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, messaging => { }, shared))
                {
                    b.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(atB.Add);
                    await b.WaitUntilConnected();

                    await using (TestHost a = await TestHost.Start(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, messaging => { }, shared))
                    {
                        IDisposable subscription = a.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(_ => { });
                        await a.WaitUntilConnected();

                        subscription.Dispose();
                        await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                    }

                    // Only B consumes now.
                    await PublishGolden(topology);

                    Assert.Equal(42, (await atB.Next(TestBus.Timeout))?.Id);
                }
            }
        }

        private static void Share(RabbitMQBusOptions options, string service)
        {
            options.QueueMode = QueueMode.Shared;
            options.SharedQueueName = service;
        }

        private static async Task WithHost(Action<MessagingBuilder> configure, Func<LegacyTopology, TestHost, Task> test)
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            await using (TestHost host = await TestHost.Start(topology.Broker.HostName, topology.Broker.Port, topology.Broker, topology.Exchange(App), App, configure))
            {
                await host.WaitUntilConnected();
                await test(topology, host);
            }
        }

        private static Task PublishGolden(LegacyTopology topology)
        {
            Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
            return topology.PublishRaw(App, topology.Exchange(App), headers, GoldenFile.Read("compat-payload.json"));
        }

        /// <summary>Publishes one message and says whether it reached the sink, i.e. wasn't dropped on arrival.</summary>
        private static async Task<bool> IsReceived(LegacyTopology topology, RecordingSink sink)
        {
            await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
            await PublishGolden(topology);
            return await sink.Next(QuietPeriod) != null;
        }
    }
}
