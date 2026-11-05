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
    /// A <see cref="RemotePubSubEvent{TPayload}"/> needs no registration: subscribing receives it. A plain
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
        public async Task SubscribingAfterInit_ReceivesThatEvent()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp receiver = topology.StartAdapter(App, null);
                AdapterApp sender = topology.StartAdapter(App, null);
                await topology.WaitUntilConnected(receiver, sender);

                Received<Order> received = receiver.Listen<SubscribedAfterInit, Order>();

                sender.PublishRemote<SubscribedAfterInit, Order>(new Order { Id = 7 });
                Assert.Equal(7, (await received.Next(TestBus.Timeout))?.Id);
            }
        }

        [Fact]
        public async Task SubscribingBeforeInit_Receives_WithNothingRegistered()
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
                    for (int i = 0; i < 100 && !service.IsConsumerConnected; i++)
                    {
                        await Task.Delay(100, TestContext.Current.CancellationToken);
                    }

                    sender.PublishRemote<SubscribedBeforeInit, Order>(new Order { Id = 8 });

                    Assert.Equal(8, (await received.Next(TestBus.Timeout))?.Id);
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
    }
}
