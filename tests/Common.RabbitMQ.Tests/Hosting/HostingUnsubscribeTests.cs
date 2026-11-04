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
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    using Messaging.Prism;

    /// <summary>The modern side: when the last subscriber goes, the binding goes, unless a registration still wants the type.</summary>
    public class HostingUnsubscribeTests
    {
        private const string App = "hosting-unsubscribe";
        private const string WireName = "Compat.Events.CompatEvent";

        private static readonly TimeSpan SettlePeriod = TimeSpan.FromMilliseconds(500);

        [Fact]
        public async Task DisposingTheLastSubscription_RemovesTheBinding_AndSubscribingAgainRestoresIt()
        {
            await WithHost(messaging => { }, async (topology, host) =>
            {
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                IDisposable first = subscriber.Subscribe<CompatMessage>(_ => { });
                IDisposable second = subscriber.Subscribe<CompatMessage>(_ => { });
                await WaitForBinding(topology, true);

                first.Dispose();
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));

                second.Dispose();
                await WaitForBinding(topology, false);

                subscriber.Subscribe<CompatMessage>(_ => { });
                await WaitForBinding(topology, true);
            });
        }

        [Fact]
        public async Task AddMessages_KeepsTheBinding_AfterTheLastSubscription()
        {
            await WithHost(messaging => messaging.AddMessages(typeof(CompatMessage).Assembly), async (topology, host) =>
            {
                IDisposable subscription = host.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(_ => { });
                await WaitForBinding(topology, true);

                subscription.Dispose();
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);

                Assert.True(await topology.IsRoutable(App, WireName));
            });
        }

        [Fact]
        public async Task MessageEvent_LastUnsubscribe_RemovesTheBinding_UnlessAnIMessageSubscriberStillListens()
        {
            EventAggregator eventAggregator = new EventAggregator();
            await WithHost(messaging => messaging.UseEventAggregator(eventAggregator), async (topology, host) =>
            {
                PubSubEvent<CompatMessage> messageEvent = eventAggregator.GetEvent<MessageEvent<CompatMessage>>();
                SubscriptionToken token = messageEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                IDisposable subscription = host.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(_ => { });
                token.Dispose();
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));

                subscription.Dispose();
                await WaitForBinding(topology, false);

                messageEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);
            });
        }

        [Fact]
        public async Task MessageEvent_SubscribedInAnotherAggregator_DoesNotBindThisApplication()
        {
            EventAggregator eventAggregator = new EventAggregator();
            new EventAggregator().GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => { }, keepSubscriberReferenceAlive: true);

            await WithHost(messaging => messaging.UseEventAggregator(eventAggregator), async (topology, host) =>
            {
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.False(await topology.IsRoutable(App, WireName));
            });
        }

        [Fact]
        public async Task Receive_IsCounted_TheBindingGoesWithTheLastStopReceiving()
        {
            await WithHost(messaging => { }, async (topology, host) =>
            {
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                subscriber.Receive(typeof(CompatMessage));
                subscriber.Receive(typeof(CompatMessage));
                await WaitForBinding(topology, true);

                subscriber.StopReceiving(typeof(CompatMessage));
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));

                subscriber.StopReceiving(typeof(CompatMessage));
                await WaitForBinding(topology, false);
            });
        }

        [Fact]
        public async Task MessageEvent_RefusedForAWireNameConflict_LeavesNothingBehind_AndDoesNotAffectTheOtherType()
        {
            EventAggregator eventAggregator = new EventAggregator();
            await WithHost(messaging => messaging.UseEventAggregator(eventAggregator), async (topology, host) =>
            {
                host.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(_ => { });
                await WaitForBinding(topology, true);

                MessageEvent<Employees.Contracts.CompatMessageTwin> twin = eventAggregator.GetEvent<MessageEvent<Employees.Contracts.CompatMessageTwin>>();
                Action<Employees.Contracts.CompatMessageTwin> handler = _ => { };

                Assert.Throws<InvalidOperationException>(() => twin.Subscribe(handler, keepSubscriberReferenceAlive: true));
                Assert.False(twin.Contains(handler));
                Assert.Throws<InvalidOperationException>(() => twin.Subscribe(handler, keepSubscriberReferenceAlive: true));

                twin.Unsubscribe(handler);
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));
            });
        }

        [Fact]
        public async Task HostedServices_StartTheReceiver_BeforeTheBuses()
        {
            EventAggregator eventAggregator = new EventAggregator();
            await WithHost(messaging => messaging.UseEventAggregator(eventAggregator), (topology, host) =>
            {
                List<string> started = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

                Assert.True(started.IndexOf("MessageEventReceiver") < started.IndexOf("MessagingHostedService"), string.Join(", ", started));
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task SharedQueue_MessageEventSubscribedBeforeStart_ReceivesWhatIsAlreadyOnTheQueue()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");
                string exchange = topology.Exchange(App);
                Action<RabbitMQBusOptions> shared = options => Share(options, service);

                await using (TestHost previous = await TestHost.Start(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, messaging => messaging.AddMessages(typeof(CompatMessage).Assembly), shared))
                {
                    await previous.WaitUntilConnected();
                }

                Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
                await topology.PublishRaw(App, WireName, headers, GoldenFile.Read("compat-payload.json"));

                EventAggregator eventAggregator = new EventAggregator();
                Received<CompatMessage> received = new Received<CompatMessage>();
                eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(received.Add, keepSubscriberReferenceAlive: true);

                await using (TestHost next = await TestHost.Start(topology.Broker.HostName, topology.Broker.Port, topology.Broker, exchange, App, messaging => messaging.UseEventAggregator(eventAggregator), shared))
                {
                    Assert.Equal(42, (await received.Next(TestBus.Timeout))?.Id);
                    Assert.Null(await topology.Get(service + ".dead-letter"));
                }
            }
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
        public async Task SharedQueue_OneInstanceUnsubscribing_KeepsTheBindingForTheOthers()
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

                    // Only B consumes now, so this reaches it if the queue is still bound.
                    Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
                    await topology.PublishRaw(App, WireName, headers, GoldenFile.Read("compat-payload.json"));

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

        private static async Task WaitForBinding(LegacyTopology topology, bool bound)
        {
            for (int i = 0; i < 100; i++)
            {
                if (await topology.IsRoutable(App, WireName) == bound)
                {
                    return;
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            Assert.Fail(bound ? "The message was never bound." : "The binding was never removed.");
        }
    }
}
