using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AppA.Events;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.LegacyModel;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// When the last subscriber of a <see cref="RemotePubSubEvent{TPayload}"/> goes, its binding goes. The application's
    /// queue is the only one on the exchange, so "routable" means "this application has the binding".
    /// </summary>
    public class RemoteEventUnsubscribeTests
    {
        private const string App = "unsubscribe";

        /// <summary>Long enough for a wrongly started binding change to reach the broker.</summary>
        private static readonly TimeSpan SettlePeriod = TimeSpan.FromMilliseconds(500);

        private static readonly string WireName = typeof(ComesAndGoes).FullName;

        [Fact]
        public async Task UnsubscribingWithTheToken_RemovesTheBinding_AndSubscribingAgainRestoresIt()
        {
            await WithApp(async (topology, app) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();

                SubscriptionToken token = remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                remoteEvent.Unsubscribe(token);
                await WaitForBinding(topology, false);

                Received<Order> received = new Received<Order>();
                remoteEvent.Subscribe(received.Add, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                AdapterApp sender = topology.StartAdapter(App, null);
                await topology.WaitUntilConnected(sender);
                sender.PublishRemote<ComesAndGoes, Order>(new Order { Id = 9 });

                Order order;
                do
                {
                    // The probes above arrive too, with empty bodies.
                    order = await received.Next(TestBus.Timeout);
                }
                while (order == null && received.Count < 200);

                Assert.Equal(9, order?.Id);
            });
        }

        [Fact]
        public async Task DisposingTheToken_RemovesTheBinding()
        {
            await WithApp(async (topology, app) =>
            {
                SubscriptionToken token = app.Events.GetEvent<ComesAndGoes>().Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                token.Dispose();

                await WaitForBinding(topology, false);
            });
        }

        [Fact]
        public async Task UnsubscribingAnAction_RemovesTheBinding_OnlyWithTheLastSubscriber()
        {
            await WithApp(async (topology, app) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                Action<Order> first = _ => { };
                Action<Order> second = _ => { };
                remoteEvent.Subscribe(first, keepSubscriberReferenceAlive: true);
                remoteEvent.Subscribe(second, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                remoteEvent.Unsubscribe(first);
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));

                remoteEvent.Unsubscribe(second);
                await WaitForBinding(topology, false);
            });
        }

        [Fact]
        public async Task ACollectedSubscriber_RemovesTheBinding_AtTheNextMessage()
        {
            await WithApp(async (topology, app) =>
            {
                SubscribeWeakly(app.Events);
                await WaitForBinding(topology, true);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // WaitForBinding's probes are the next messages.
                await WaitForBinding(topology, false);
            });
        }

        [Fact]
        public async Task QuickChanges_EndInTheLastState()
        {
            await WithApp(async (topology, app) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                SubscriptionToken token = null;
                for (int i = 0; i < 30; i++)
                {
                    token = remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                    token.Dispose();
                }

                token = remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));

                for (int i = 0; i < 30; i++)
                {
                    token.Dispose();
                    token = remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                }

                token.Dispose();
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.False(await topology.IsRoutable(App, WireName));
            });
        }

        [Fact]
        public async Task SubscribingAfterUnregisterQueue_Resumes()
        {
            await WithApp(async (topology, app) =>
            {
                PubSubEvent<Order> remoteEvent = app.Events.GetEvent<ComesAndGoes>();
                remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                await app.Service.UnregisterQueue(typeof(ComesAndGoes));
                Assert.False(await topology.IsRoutable(App, WireName));

                remoteEvent.Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);
            });
        }

        [Fact]
        public async Task RegisterQueue_KeepsTheBinding_AfterTheLastSubscriber_UntilUnregisterQueue()
        {
            await WithApp(async (topology, app) =>
            {
                await app.Service.RegisterQueue(typeof(ComesAndGoes));
                SubscriptionToken token = app.Events.GetEvent<ComesAndGoes>().Subscribe(_ => { }, keepSubscriberReferenceAlive: true);
                await WaitForBinding(topology, true);

                token.Dispose();
                await Task.Delay(SettlePeriod, TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutable(App, WireName));

                await app.Service.UnregisterQueue(typeof(ComesAndGoes));
                await WaitForBinding(topology, false);
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeWeakly(IEventAggregator eventAggregator)
        {
            int seen = 0;
            eventAggregator.GetEvent<ComesAndGoes>().Subscribe(_ => seen++);
        }

        private static async Task WithApp(Func<LegacyTopology, AdapterApp, Task> test)
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp app = topology.StartAdapter(App, null);
                await topology.WaitUntilConnected(app);
                await test(topology, app);
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

            Assert.Fail(bound ? "The event was never bound." : "The binding was never removed.");
        }
    }
}
