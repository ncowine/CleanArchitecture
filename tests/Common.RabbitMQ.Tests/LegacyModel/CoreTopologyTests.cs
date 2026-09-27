using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AppA.Events;
using AppB.Events;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Messaging;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// The core in the ADR 0002 topology (delivery plan step 6), against the legacy model: own exchange with a configurable
    /// type, subscriptions to other applications' exchanges that are checked but never declared, and routing keys per
    /// message. Assumption IDs refer to docs/legacy-baseline-assumptions.md.
    /// </summary>
    public class CoreTopologyTests
    {
        private const string AppA = "AppA";
        private const string AppB = "AppB";
        private const string AppC = "AppC";
        private const string OrderSavedWireName = "AppA.Events.OrderSaved";

        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        private static readonly Assembly AppAEvents = typeof(OrderSaved).Assembly;
        private static readonly Assembly AppBEvents = typeof(CustomerChanged).Assembly;

        [Fact]
        public async Task CoreOwner_LegacySubscriberReceives()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                CoreApp owner = topology.CreateCore(AppA).Start();
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> atSubscriber = subscriber.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnected(owner);
                await topology.WaitUntilConnected(subscriber);
                WireCapture capture = await topology.Capture(AppA);

                owner.Publish(OrderSavedWireName, GoldenOrder());

                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.Equal(topology.Exchange(AppA), message.Exchange);
                Assert.Equal(OrderSavedWireName, message.RoutingKey);
                Assert.Equal(OrderSavedWireName, message.Headers[WireHeaders.MessageType]);
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
            }
        }

        [Fact]
        public async Task LegacyOwner_CoreSubscriberReceives()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp owner = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(owner);

                CoreApp subscriber = topology.CreateCore(AppB, options => options.Subscriptions.Add(new SubscriptionOptions { Exchange = topology.Exchange(AppA) }));
                Received<Order> atSubscriber = subscriber.Listen<Order>(OrderSavedWireName);
                subscriber.Start();
                await topology.WaitUntilConnected(subscriber);

                owner.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
                MessageContext context = subscriber.Contexts.Single();
                Assert.Equal(topology.Exchange(AppA), context.Exchange);
                Assert.Equal(OrderSavedWireName, context.RoutingKey);
                Assert.Equal(OrderSavedWireName, context.WireName);
            }
        }

        [Fact]
        public async Task CoreSubscriber_NeverDeclaresAnotherAppsExchange_AndConnectsOnceTheOwnerStarts()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                RecordingObserver observer = new RecordingObserver();
                CoreApp subscriber = topology.CreateCore(AppB, options => options.Subscriptions.Add(new SubscriptionOptions { Exchange = topology.Exchange(AppA) }), observer);
                Received<Order> atSubscriber = subscriber.Listen<Order>(OrderSavedWireName);
                subscriber.Start();

                ObservedEvent failed = await observer.WaitFor(
                    nameof(IMessagingObserver.OnConnectionChanged),
                    TestBus.Timeout,
                    e => e.Connection.Role == ConnectionRole.Consumer && e.Connection.Status == ConnectionStatus.Failed);
                Assert.NotNull(failed);
                Assert.Contains("does not exist yet", failed.Connection.Error.Message);

                // Unlike a legacy subscriber (A16), the core left AppA's exchange alone.
                Assert.Equal(404, await topology.ExchangeExists(topology.Exchange(AppA)));

                LegacyApp owner = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(owner);
                await topology.WaitUntilConnected(subscriber);

                owner.PublishRemote<OrderSaved, Order>(GoldenOrder());
                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
            }
        }

        [Fact]
        public async Task CustomRoutingKey_ReachesSubscribersThatBindIt_NotLegacyPerEventSubscribers()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                CoreApp owner = topology.CreateCore(AppA).Start();
                CoreApp patternSubscriber = topology.CreateCore(AppB, options => options.Subscriptions.Add(new SubscriptionOptions
                {
                    Exchange = topology.Exchange(AppA),
                    RoutingKeys = { "orders.*.saved" },
                }));
                Received<Order> atPatternSubscriber = patternSubscriber.Listen<Order>(OrderSavedWireName);
                patternSubscriber.Start();
                LegacyApp legacySubscriber = topology.Start(AppC, new[] { AppA }, AppAEvents);
                Received<Order> atLegacySubscriber = legacySubscriber.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnected(owner, patternSubscriber);
                await topology.WaitUntilConnected(legacySubscriber);

                // A custom key: only the subscriber binding "orders.*.saved" gets it. The event-type header still says
                // what it is, so it arrives as an OrderSaved.
                owner.Publish(OrderSavedWireName, GoldenOrder(), "orders.eu.saved");
                AssertGolden(await atPatternSubscriber.Next(TestBus.Timeout));
                Assert.Equal("orders.eu.saved", patternSubscriber.Contexts.Single().RoutingKey);

                // The default key (the wire name): only the legacy per-event binding (A9) matches.
                owner.Publish(OrderSavedWireName, GoldenOrder());
                AssertGolden(await atLegacySubscriber.Next(TestBus.Timeout));

                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, atPatternSubscriber.Count);
                Assert.Equal(1, atLegacySubscriber.Count);
            }
        }

        [Theory]
        [InlineData("topic")]
        [InlineData("direct")]
        [InlineData("fanout")]
        [InlineData("headers")]
        public async Task ExchangeTypes_OwnerDeclaresItsType_SubscriberBindsAccordingly(string exchangeType)
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                CoreApp owner = topology.CreateCore(AppA, options => options.ExchangeType = exchangeType).Start();
                await topology.WaitUntilConnected(owner);
                Assert.Equal(200, await topology.DeclareExchange(topology.Exchange(AppA), exchangeType, durable: true));

                CoreApp subscriber = topology.CreateCore(AppB, options => options.Subscriptions.Add(SubscriptionFor(exchangeType, topology.Exchange(AppA))));
                Received<Order> atSubscriber = subscriber.Listen<Order>(OrderSavedWireName);
                subscriber.Start();
                await topology.WaitUntilConnected(subscriber);

                owner.Publish(OrderSavedWireName, GoldenOrder());
                AssertGolden(await atSubscriber.Next(TestBus.Timeout));

                // What else the binding lets through: no other queue is bound to AppA's exchange.
                bool expectRoutable = exchangeType == "topic" || exchangeType == "fanout";
                Assert.Equal(expectRoutable, await topology.IsRoutable(AppA, "AppA.Events.Other"));
            }
        }

        [Fact]
        public async Task A16_Consequence_LegacySubscriberCannotJoinANonTopicExchange()
        {
            // Legacy subscribers declare the exchanges they subscribe to as topic (A16). An owner that picks another
            // type locks them out: RabbitMQ rejects the conflicting declaration. Non-topic exchanges are only safe
            // where no legacy application subscribes.
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                CoreApp owner = topology.CreateCore(AppA, options => options.ExchangeType = "direct").Start();
                await topology.WaitUntilConnected(owner);

                LegacyApp legacySubscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

                Assert.False(legacySubscriber.IsConnected);
            }
        }

        [Fact]
        public async Task SharedQueue_WithPatternSubscription_SkipsUnhandledTypes_InsteadOfDeadLettering()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp owner = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(owner);

                string service = "svc-" + Guid.NewGuid().ToString("N");
                CoreApp subscriber = topology.CreateCore(AppB, options =>
                {
                    options.QueueMode = QueueMode.Shared;
                    options.SharedQueueName = service;
                    options.Subscriptions.Add(new SubscriptionOptions { Exchange = topology.Exchange(AppA), RoutingKeys = { "AppA.Events.#" } });
                });
                Received<Order> atSubscriber = subscriber.Listen<Order>(OrderSavedWireName);
                subscriber.Start();
                topology.DeleteOnDispose(service, service + ".dead-letter");
                await topology.WaitUntilConnected(subscriber);

                // Matched by the pattern, but not a type this service handles.
                Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = "AppA.Events.Unhandled" };
                await topology.PublishRaw(AppA, "AppA.Events.Unhandled", headers, GoldenFile.Read("compat-payload.json"));
                owner.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Null(await topology.Get(service + ".dead-letter"));
                Assert.Equal(1, atSubscriber.Count);
            }
        }

        private static SubscriptionOptions SubscriptionFor(string exchangeType, string exchange)
        {
            SubscriptionOptions subscription = new SubscriptionOptions { Exchange = exchange };
            switch (exchangeType)
            {
                case "topic":
                    subscription.RoutingKeys.Add("AppA.Events.*");
                    break;
                case "direct":
                    subscription.RoutingKeys.Add(OrderSavedWireName);
                    break;
                case "fanout":
                    subscription.RoutingKeys.Add("ignored-by-fanout");
                    break;
                default:
                    // Every message the core sends carries event-type, so it is a natural header to match.
                    subscription.HeaderMatch[WireHeaders.MessageType] = OrderSavedWireName;
                    break;
            }

            return subscription;
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
