using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AppA.Events;
using AppB.Events;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using RabbitMQ.Client;
using Shared.Events;
using Xunit;
using LegacyHeaders = Legacy.RabbitMQ.MessageHeaders;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// What the legacy model does on a real broker. It stands in for the real legacy library (ADR 0002, step 5), so
    /// every test is named after the fact (F) or assumption (A) it pins in docs/legacy-baseline-assumptions.md.
    /// When the real behaviour turns out to differ, change the model, the document and the matching test together.
    /// </summary>
    public class LegacyModelSpecTests
    {
        private const string AppA = "AppA";
        private const string AppB = "AppB";
        private const string AppC = "AppC";

        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        private static readonly Assembly AppAEvents = typeof(OrderSaved).Assembly;
        private static readonly Assembly AppBEvents = typeof(CustomerChanged).Assembly;
        private static readonly Assembly SharedEvents = typeof(UserChanged).Assembly;

        [Fact]
        public async Task F2_A11_A15_SubscriberReceivesFromTheOwnersExchange_SenderDoesNotGetItBack()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp otherInstance = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> sent = sender.Listen<OrderSaved, Order>();
                Received<Order> atOtherInstance = otherInstance.Listen<OrderSaved, Order>();
                Received<Order> atSubscriber = subscriber.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnected(sender, otherInstance, subscriber);

                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                // A15: local subscribers first, synchronously.
                Assert.Equal(1, sent.Count);
                AssertGolden(await atOtherInstance.Next(TestBus.Timeout));
                AssertGolden(await atSubscriber.Next(TestBus.Timeout));

                // A11: the sender's own copy comes back through its own binding and is dropped.
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, sent.Count);
                Assert.Equal(1, atOtherInstance.Count);
                Assert.Equal(1, atSubscriber.Count);
            }
        }

        [Fact]
        public async Task F2_AnAppThatDoesNotSubscribe_ReceivesNothing_EvenIfItKnowsTheEvent()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                LegacyApp bystander = topology.Start(AppC, new string[0], AppAEvents);
                Received<Order> atSubscriber = subscriber.Listen<OrderSaved, Order>();
                Received<Order> atBystander = bystander.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnected(sender, subscriber, bystander);

                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await atSubscriber.Next(TestBus.Timeout));
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(0, atBystander.Count);
            }
        }

        [Fact]
        public async Task F2_SharedEvents_TravelOnThePublishersOwnExchange()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp sender = topology.Start(AppB, new string[0], AppBEvents, SharedEvents);
                LegacyApp subscriber = topology.Start(AppA, new[] { AppB }, AppAEvents, SharedEvents);
                Received<User> atSubscriber = subscriber.Listen<UserChanged, User>();
                await topology.WaitUntilConnected(sender, subscriber);
                WireCapture capture = await topology.Capture(AppB);

                sender.PublishRemote<UserChanged, User>(new User { Id = 7, Name = "shared", UpdatedAt = new DateTime(2026, 9, 26, 10, 15, 30, DateTimeKind.Utc) });

                User received = await atSubscriber.Next(TestBus.Timeout);
                Assert.Equal("shared", received?.Name);
                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.Equal(topology.Exchange(AppB), message?.Exchange);
            }
        }

        [Fact]
        public async Task A1_OwnExchange_IsDeclaredByItsApp_AsDurableTopic()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp app = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(app);

                Assert.Equal(200, await topology.DeclareExchange(app.ExchangeName, ExchangeType.Topic, durable: true));

                // 406 PRECONDITION_FAILED: the exchange exists with different arguments.
                Assert.Equal(406, await topology.DeclareExchange(app.ExchangeName, ExchangeType.Direct, durable: true));
                Assert.Equal(406, await topology.DeclareExchange(app.ExchangeName, ExchangeType.Topic, durable: false));
            }
        }

        [Fact]
        public async Task A16_SubscribedExchange_IsDeclaredByTheSubscriber_BeforeItsOwnerStarts()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                await topology.WaitUntilConnected(subscriber);

                // AppA never started, yet its exchange exists, declared as a durable topic by AppB.
                Assert.Equal(200, await topology.ExchangeExists(topology.Exchange(AppA)));
                Assert.Equal(200, await topology.DeclareExchange(topology.Exchange(AppA), ExchangeType.Topic, durable: true));
            }
        }

        [Fact]
        public async Task A2_A3_A4_A5_PublishedMessage_OnTheWire()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(sender);
                WireCapture capture = await topology.Capture(AppA);

                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(sender.ExchangeName, message.Exchange);
                Assert.Equal("AppA.Events.OrderSaved", message.RoutingKey);                         // A2
                Assert.Equal("AppA.Events.OrderSaved", message.Headers[LegacyHeaders.EventType]);   // A3
                Assert.Equal(sender.InstanceId, message.Headers[LegacyHeaders.SourceId]);           // A3
                Assert.Equal("application/json", message.ContentType);                               // A4
                Assert.Equal(AppA, message.AppId);                                                   // A4
                Assert.False(string.IsNullOrEmpty(message.MessageId));                               // A4
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);                  // A5
            }
        }

        [Fact]
        public async Task A7_Queue_IsExclusivePerProcess_AndRemovedWhenTheAppStops()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp app = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnected(app);

                Assert.Equal($"appa.{app.InstanceId}", app.QueueName);
                Assert.Equal(405, await topology.PassiveDeclareQueue(app.QueueName));

                app.Dispose();
                ushort replyCode = 0;
                for (int attempt = 0; attempt < 50 && replyCode != 404; attempt++)
                {
                    replyCode = await topology.PassiveDeclareQueue(app.QueueName);
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                Assert.Equal(404, replyCode);
            }
        }

        [Fact]
        public async Task A8_A9_Bindings_OnePerKnownEvent_OnOwnAndSubscribedExchanges()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                // The only queue in this test: AppB's. So "routable" means "AppB has a binding for it".
                LegacyApp app = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                await topology.WaitUntilConnected(app);

                // A8: own exchange, every known event (including other apps' events it knows).
                Assert.True(await topology.IsRoutable(AppB, "AppB.Events.CustomerChanged"));
                Assert.True(await topology.IsRoutable(AppB, "AppA.Events.OrderSaved"));

                // A9: subscribed exchange, the same per-event bindings, nothing else.
                Assert.True(await topology.IsRoutable(AppA, "AppA.Events.OrderSaved"));
                Assert.True(await topology.IsRoutable(AppA, "AppA.Events.OrderSelected"));
                Assert.True(await topology.IsRoutable(AppA, "AppB.Events.CustomerChanged"));
                Assert.False(await topology.IsRoutable(AppA, "AppA.Events.NotAnEvent"));
                Assert.False(await topology.IsRoutable(AppA, "orders.eu.saved"));
            }
        }

        [Fact]
        public async Task A10_EventIsResolvedFromTheHeader_ThenFromTheRoutingKey()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp app = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> orders = app.Listen<OrderSaved, Order>();
                Received<Customer> customers = app.Listen<CustomerChanged, Customer>();
                await topology.WaitUntilConnected(app);
                byte[] body = GoldenFile.Read("compat-payload.json");

                // Routed by the OrderSaved binding, but the header says CustomerChanged: the header wins.
                Dictionary<string, object> headers = new Dictionary<string, object> { [LegacyHeaders.EventType] = "AppB.Events.CustomerChanged" };
                await topology.PublishRaw(AppA, "AppA.Events.OrderSaved", headers, body);
                Customer customer = await customers.Next(TestBus.Timeout);
                Assert.Equal(42, customer?.Id);

                // No header: the routing key decides.
                await topology.PublishRaw(AppA, "AppA.Events.OrderSaved", null, body);
                AssertGolden(await orders.Next(TestBus.Timeout));

                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, orders.Count);
                Assert.Equal(1, customers.Count);
            }
        }

        [Fact]
        public async Task A12_FailingSubscriber_MessageIsAcked_AndNotRedelivered()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> received = subscriber.Listen<OrderSaved, Order>();
                received.OnReceiving = order =>
                {
                    if (order.Name == "fail")
                    {
                        throw new InvalidOperationException("Subscriber failed on purpose.");
                    }
                };
                await topology.WaitUntilConnected(sender, subscriber);

                Order failing = GoldenOrder();
                failing.Name = "fail";
                sender.PublishRemote<OrderSaved, Order>(failing);
                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await received.Next(TestBus.Timeout));
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);

                // One failed call, one successful: the failed message was acked, not redelivered.
                Assert.Equal(2, received.Count);
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
            Assert.Equal(DateTimeKind.Utc, order.UpdatedAt.Kind);
        }
    }
}
