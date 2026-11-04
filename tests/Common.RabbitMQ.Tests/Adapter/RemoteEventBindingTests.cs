using System;
using System.Threading.Tasks;
using AppA.Events;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.LegacyModel;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// A <see cref="RemotePubSubEvent{TPayload}"/> needs no registration: subscribing binds it. A plain
    /// <see cref="PubSubEvent{TPayload}"/> stays local unless its assembly is added to the registry.
    /// </summary>
    [Collection(ServiceProviderCollection.Name)]
    public class RemoteEventBindingTests : IDisposable
    {
        private const string App = "bind-on-subscribe";

        public void Dispose()
        {
            RabbitMQServiceProvider.Current = null;
        }

        [Fact]
        public async Task SubscribingAfterInit_BindsThatEventOnly_AndItArrives()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp receiver = topology.StartAdapter(App, null);
                AdapterApp sender = topology.StartAdapter(App, null);
                await topology.WaitUntilConnected(receiver, sender);

                Assert.False(await topology.IsRoutable(App, typeof(SubscribedAfterInit).FullName));

                Received<Order> received = receiver.Listen<SubscribedAfterInit, Order>();
                await WaitUntilRoutable(topology, typeof(SubscribedAfterInit));

                Assert.False(await topology.IsRoutable(App, typeof(NeverSubscribed).FullName));

                sender.PublishRemote<SubscribedAfterInit, Order>(new Order { Id = 7 });
                Assert.Equal(7, (await NextPublished(received))?.Id);
            }
        }

        [Fact]
        public async Task Subscribing_InAnotherApplication_DoesNotBindThisOne()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                new EventAggregator().GetEvent<SubscribedInAnotherApp>().Subscribe(_ => { });

                AdapterApp app = topology.StartAdapter(App, null);
                await topology.WaitUntilConnected(app);

                Assert.False(await topology.IsRoutable(App, typeof(SubscribedInAnotherApp).FullName));
            }
        }

        [Fact]
        public async Task SubscribingBeforeInit_BindsWhenItConnects_WithNothingRegistered()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                EventAggregator eventAggregator = new EventAggregator();
                Received<Order> received = new Received<Order>();
                eventAggregator.GetEvent<SubscribedBeforeInit>().Subscribe(received.Add, ThreadOption.PublisherThread, keepSubscriberReferenceAlive: true);

                using (RabbitMQService service = new RabbitMQService(eventAggregator, new RemoteEventRegistry()))
                {
                    service.Init(topology.Broker.CreateCurrentConfig("Main", topology.Exchange(App), App));
                    AdapterApp sender = topology.StartAdapter(App, null);
                    await topology.WaitUntilConnected(sender);
                    await WaitUntilRoutable(topology, typeof(SubscribedBeforeInit));

                    sender.PublishRemote<SubscribedBeforeInit, Order>(new Order { Id = 8 });

                    Assert.Equal(8, (await NextPublished(received))?.Id);
                }
            }
        }

        [Fact]
        public void PublishRemote_OnARemoteEvent_NeedsNoRegistration()
        {
            using (RabbitMQService service = new RabbitMQService(new EventAggregator(), new RemoteEventRegistry()))
            {
                service.Init(new RabbitMQConfig { BusName = "Main", ExchangeName = "binding-tests", ClientName = "binding-tests", HostName = "127.0.0.1", Port = 1 });
                RabbitMQServiceProvider.Current = service;

                new EventAggregator().GetEvent<NeverSubscribed>().PublishRemote(new Order { Id = 1 });

                Assert.Equal(1, service.OutstandingCount);
            }
        }

        [Fact]
        public void PublishRemote_OnAPlainEvent_IsRefused_UnlessItsAssemblyIsAdded()
        {
            using (RabbitMQService service = new RabbitMQService(new EventAggregator(), new RemoteEventRegistry()))
            {
                service.Init(new RabbitMQConfig { BusName = "Main", ExchangeName = "binding-tests", ClientName = "binding-tests", HostName = "127.0.0.1", Port = 1 });
                RabbitMQServiceProvider.Current = service;

                ArgumentException error = Assert.Throws<ArgumentException>(() => new EventAggregator().GetEvent<OrderSaved>().PublishRemote(new Order { Id = 1 }));

                Assert.Contains("local", error.Message);
                Assert.Equal(0, service.OutstandingCount);
            }
        }

        /// <summary>Skips the routability probes, whose empty bodies are delivered as null once the event is bound.</summary>
        private static async Task<Order> NextPublished(Received<Order> received)
        {
            for (int i = 0; i < 200; i++)
            {
                Order order = await received.Next(TestBus.Timeout);
                if (order != null)
                {
                    return order;
                }
            }

            return null;
        }

        private static async Task WaitUntilRoutable(LegacyTopology topology, Type eventType)
        {
            for (int i = 0; i < 100; i++)
            {
                if (await topology.IsRoutable(App, eventType.FullName))
                {
                    return;
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"{eventType.Name} was never bound.");
        }
    }
}
