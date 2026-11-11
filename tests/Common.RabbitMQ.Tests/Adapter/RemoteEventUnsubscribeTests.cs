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
    /// Unsubscribing from a <see cref="RemotePubSubEvent{TPayload}"/> never touches the broker: the queue is bound once per
    /// exchange. These check that the event keeps arriving, or arrives again, whatever order subscribers come and go in.
    /// </summary>
    public class RemoteEventUnsubscribeTests
    {
        private const string App = "unsubscribe";

        [Fact]
        public async Task UnsubscribingWithTheToken_ThenSubscribingAgain_Receives()
        {
            await WithAppAsync(async (topology, app, sender) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                remoteEvent.Unsubscribe(remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true));

                Received<Order> received = new Received<Order>();
                remoteEvent.Subscribe(received.Add, keepSubscriberReferenceAlive: true);
                sender.PublishRemote<ComesAndGoes, Order>(new Order { Id = 9 });

                Assert.Equal(9, (await received.NextAsync(TestBus.Timeout))?.Id);
            });
        }

        [Fact]
        public async Task UnsubscribingOneAction_TheOtherStillReceives()
        {
            await WithAppAsync(async (topology, app, sender) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                Action<Order> first = _ => { };
                Received<Order> second = new Received<Order>();
                remoteEvent.Subscribe(first, keepSubscriberReferenceAlive: true);
                remoteEvent.Subscribe(second.Add, keepSubscriberReferenceAlive: true);

                remoteEvent.Unsubscribe(first);
                sender.PublishRemote<ComesAndGoes, Order>(new Order { Id = 10 });

                Assert.Equal(10, (await second.NextAsync(TestBus.Timeout))?.Id);
            });
        }

        [Fact]
        public async Task QuickChanges_EndInTheLastState()
        {
            await WithAppAsync(async (topology, app, sender) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                for (int i = 0; i < 30; i++)
                {
                    remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true).Dispose();
                }

                Received<Order> received = new Received<Order>();
                remoteEvent.Subscribe(received.Add, keepSubscriberReferenceAlive: true);
                sender.PublishRemote<ComesAndGoes, Order>(new Order { Id = 11 });

                Assert.Equal(11, (await received.NextAsync(TestBus.Timeout))?.Id);
            });
        }

        [Fact]
        public async Task SubscribingAfterUnregisterQueue_Resumes()
        {
            await WithAppAsync(async (topology, app, sender) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await app.Service.UnregisterQueue(typeof(ComesAndGoes));

                Received<Order> received = new Received<Order>();
                remoteEvent.Subscribe(received.Add, keepSubscriberReferenceAlive: true);
                sender.PublishRemote<ComesAndGoes, Order>(new Order { Id = 12 });

                Assert.Equal(12, (await received.NextAsync(TestBus.Timeout))?.Id);
            });
        }

        private static async Task WithAppAsync(Func<LegacyTopology, AdapterApp, AdapterApp, Task> test)
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                AdapterApp app = topology.StartAdapter(App, null);
                AdapterApp sender = topology.StartAdapter(App, null);
                await topology.WaitUntilConnectedAsync(app, sender);
                await test(topology, app, sender);
            }
        }
    }
}
