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
    /// The modern side: when the last subscriber goes, the type is dropped on arrival again. The queue's binding (one per
    /// exchange) never changes, so only what the subscribers see shows what is received.
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
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            await using (TestHost host = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, topology.Exchange(App), App))
            {
                await host.WaitUntilConnectedAsync();
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                Received<CompatMessage> atFirst = new Received<CompatMessage>();
                Received<CompatMessage> atSecond = new Received<CompatMessage>();
                IDisposable first = subscriber.Subscribe<CompatMessage>(atFirst.Add);
                IDisposable second = subscriber.Subscribe<CompatMessage>(atSecond.Add);

                await PublishAfterSettlingAsync(topology);
                Assert.NotNull(await atFirst.NextAsync(TestBus.Timeout));
                Assert.NotNull(await atSecond.NextAsync(TestBus.Timeout));

                first.Dispose();
                await PublishAfterSettlingAsync(topology);
                Assert.NotNull(await atSecond.NextAsync(TestBus.Timeout));
                Assert.Null(await atFirst.NextAsync(QuietPeriod));

                // With no subscriber left the message is dropped on arrival, so the next subscriber gets only its own.
                second.Dispose();
                await PublishAfterSettlingAsync(topology);
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);

                Received<CompatMessage> atThird = new Received<CompatMessage>();
                subscriber.Subscribe<CompatMessage>(atThird.Add);
                await PublishAfterSettlingAsync(topology);
                Assert.NotNull(await atThird.NextAsync(TestBus.Timeout));
                Assert.Null(await atThird.NextAsync(QuietPeriod));
            }
        }

        [Fact]
        public async Task SharedQueue_ASecondClassWithTheSameWireName_IsRefused_AfterTheFirstUnsubscribes()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");

                await using (TestHost host = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, topology.Exchange(App), App, null, options => Share(options, service)))
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

                await using (TestHost b = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, null, shared))
                {
                    b.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(atB.Add);
                    await b.WaitUntilConnectedAsync();

                    await using (TestHost a = await TestHost.StartAsync(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, null, shared))
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

        private static Task PublishGoldenAsync(LegacyTopology topology)
        {
            Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
            return topology.PublishRawAsync(App, topology.Exchange(App), headers, GoldenFile.Read("compat-payload.json"));
        }

        private static async Task PublishAfterSettlingAsync(LegacyTopology topology)
        {
            await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
            await PublishGoldenAsync(topology);
        }
    }
}
