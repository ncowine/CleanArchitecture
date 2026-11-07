using System;
using System.Reflection;
using System.Threading.Tasks;
using AppA.Events;
using AppB.Events;
using Common.RabbitMQ.Tests.Broker;
using Messaging.RabbitMQ;
using Shared.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// The current <c>Common.RabbitMQ</c> adapter in the ADR 0002 topology (delivery plan step 7), against the legacy
    /// model: the legacy apps keep their plain <see cref="Prism.Events.PubSubEvent{TPayload}"/> events; the adapter apps declare
    /// their own remote events with the same wire names (<see cref="RemoteOrderSaved"/>). Own exchange, subscriptions, routing keys.
    /// </summary>
    public class AdapterTopologyTests
    {
        private const string AppA = "AppA";
        private const string AppB = "AppB";
        private const string AppC = "AppC";
        private const string OrderSavedWireName = "AppA.Events.OrderSaved";

        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        private static readonly Assembly AppAEvents = typeof(OrderSaved).Assembly;
        private static readonly Assembly AppBEvents = typeof(CustomerChanged).Assembly;
        private static readonly Assembly SharedEvents = typeof(UserChanged).Assembly;

        [Fact]
        public async Task AdapterOwner_LegacySubscriberReceives_SenderNotEchoed()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp owner = topology.StartAdapter(AppA, null);
                Received<Order> sent = owner.Listen<RemoteOrderSaved, Order>();
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> atSubscriber = subscriber.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnected(owner);
                await topology.WaitUntilConnected(subscriber);

                owner.PublishRemote<RemoteOrderSaved, Order>(GoldenOrder());

                Assert.Equal(1, sent.Count);
                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, sent.Count);
            }
        }

        [Fact]
        public async Task LegacyOwner_AdapterSubscriberReceives()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp owner = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(owner);

                AdapterApp subscriber = topology.StartAdapter(
                    AppB,
                    config => config.Subscriptions.Add(new RabbitMQSubscription { Exchange = topology.Exchange(AppA) }));
                Received<Order> atSubscriber = subscriber.Listen<RemoteOrderSaved, Order>();
                await topology.WaitUntilConnected(subscriber);

                owner.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
            }
        }

        [Fact]
        public async Task PublishRemoteTo_ReachesSubscribersThatBindTheKey_AndLocalSubscribers()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp owner = topology.StartAdapter(AppA, null);
                Received<Order> sent = owner.Listen<RemoteOrderSaved, Order>();
                CoreApp patternSubscriber = topology.CreateCore(AppB, options => options.Subscriptions.Add(new SubscriptionOptions
                {
                    Exchange = topology.Exchange(AppA),
                }));
                patternSubscriber.CreateBus().Bind(topology.Exchange(AppA), "orders.*.saved");
                Received<Order> atPatternSubscriber = patternSubscriber.Listen<Order>(OrderSavedWireName);
                patternSubscriber.Start();
                LegacyApp legacySubscriber = topology.Start(AppC, new[] { AppA }, AppAEvents);
                Received<Order> atLegacySubscriber = legacySubscriber.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnected(owner);
                await topology.WaitUntilConnected(patternSubscriber);
                await topology.WaitUntilConnected(legacySubscriber);

                owner.PublishRemoteTo<RemoteOrderSaved, Order>("orders.eu.saved", GoldenOrder());

                Assert.Equal(1, sent.Count);
                AssertGolden(await atPatternSubscriber.Next(TestBus.Timeout));
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(0, atLegacySubscriber.Count);
            }
        }

        [Fact]
        public async Task SharedEvents_TravelBetweenAdapterAndLegacyModel_ByWireName()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp sender = topology.StartAdapter(AppB, null);
                LegacyApp subscriber = topology.Start(AppA, new[] { AppB }, AppAEvents, SharedEvents);
                Received<User> atSubscriber = subscriber.Listen<UserChanged, User>();
                await topology.WaitUntilConnected(sender);
                await topology.WaitUntilConnected(subscriber);

                sender.PublishRemote<RemoteUserChanged, User>(new User { Id = 7, Name = "shared", UpdatedAt = new DateTime(2026, 9, 26, 10, 15, 30, DateTimeKind.Utc) });

                Assert.Equal("shared", (await atSubscriber.Next(TestBus.Timeout))?.Name);
            }
        }

        [Fact]
        public async Task ConfiguredExchangeType_IsDeclared()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                AdapterApp owner = topology.StartAdapter(AppA, config => config.ExchangeType = "direct");
                await topology.WaitUntilConnected(owner);

                Assert.Equal(200, await topology.DeclareExchange(owner.ExchangeName, "direct", durable: true));
            }
        }

        /// <summary>The payload whose body is Golden/compat-payload.json.</summary>
        private static Order GoldenOrder()
        {
            return new Order { Id = 42, Name = "compat", UpdatedAt = new DateTime(2026, 9, 26, 10, 15, 30, DateTimeKind.Utc) };
        }

        private static void AssertGolden(Order order)
        {
            Assert.NotNull(order);
            Assert.Equal(42, order.Id);
            Assert.Equal("compat", order.Name);
            Assert.Equal(new DateTime(2026, 9, 26, 10, 15, 30, DateTimeKind.Utc), order.UpdatedAt);
        }
    }
}
