using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AppA.Events;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.LegacyModel;
using Messaging;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// Subscribing to a <see cref="RemotePubSubEvent{TPayload}"/> with <see cref="SubscribeOptions"/> binds the routing key
    /// on the broker; the last subscription with that key unbinds it.
    /// </summary>
    public class RemoteEventRoutingKeyTests
    {
        private const string App = "keyed";
        private const string Key = "orders.keyed";

        [Fact]
        public async Task SubscribingWithAKey_Receives_AndUnsubscribingUnbinds()
        {
            await WithAppAsync(async (topology, app, sender) =>
            {
                Received<Order> received = new Received<Order>();
                SubscriptionToken token = app.Events.GetEvent<KeyedFirst>()
                    .Subscribe(received.Add, new SubscribeOptions { RoutingKey = Key }, keepSubscriberReferenceAlive: true);
                Assert.True(await WaitUntilRoutableAsync(topology, true));

                sender.PublishRemoteTo<KeyedFirst, Order>(Key, new Order { Id = 7 });
                Assert.Equal(7, (await received.NextAsync(TestBus.Timeout))?.Id);

                token.Dispose();
                Assert.True(await WaitUntilRoutableAsync(topology, false));
            });
        }

        [Fact]
        public async Task TwoEventsShareAKey_TheBindingStaysUntilBothUnsubscribe()
        {
            await WithAppAsync(async (topology, app, sender) =>
            {
                SubscriptionToken first = app.Events.GetEvent<KeyedFirst>()
                    .Subscribe(_ => { }, new SubscribeOptions { RoutingKey = Key }, keepSubscriberReferenceAlive: true);
                Action<Order> second = _ => { };
                app.Events.GetEvent<KeyedSecond>().Subscribe(second, new SubscribeOptions { RoutingKey = Key }, keepSubscriberReferenceAlive: true);
                Assert.True(await WaitUntilRoutableAsync(topology, true));

                first.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
                Assert.True(await topology.IsRoutableAsync(App, Key));

                // Unsubscribing by action releases the key too.
                app.Events.GetEvent<KeyedSecond>().Unsubscribe(second);
                Assert.True(await WaitUntilRoutableAsync(topology, false));
            });
        }

        [Fact]
        public void SubscribeOptions_WithoutAKey_IsRefused_AndNothingIsSubscribed()
        {
            EventAggregator events = new EventAggregator();
            KeyedFirst keyed = events.GetEvent<KeyedFirst>();

            Assert.Throws<ArgumentException>(() => keyed.Subscribe(_ => { }, new SubscribeOptions { Exchange = "x" }));
            Assert.False(keyed.Contains(_ => { }));
        }

        private static async Task<bool> WaitUntilRoutableAsync(LegacyTopology topology, bool expected)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TestBus.Timeout)
            {
                if (await topology.IsRoutableAsync(App, Key) == expected)
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return false;
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
