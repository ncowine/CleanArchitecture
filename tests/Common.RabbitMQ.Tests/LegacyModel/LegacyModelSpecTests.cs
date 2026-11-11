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
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp otherInstance = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> sent = sender.Listen<OrderSaved, Order>();
                Received<Order> atOtherInstance = otherInstance.Listen<OrderSaved, Order>();
                Received<Order> atSubscriber = subscriber.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnectedAsync(sender, otherInstance, subscriber);

                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                // A15: local subscribers first, synchronously.
                Assert.Equal(1, sent.Count);
                AssertGolden(await atOtherInstance.NextAsync(TestBus.Timeout));
                AssertGolden(await atSubscriber.NextAsync(TestBus.Timeout));

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
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                LegacyApp bystander = topology.Start(AppC, new string[0], AppAEvents);
                Received<Order> atSubscriber = subscriber.Listen<OrderSaved, Order>();
                Received<Order> atBystander = bystander.Listen<OrderSaved, Order>();
                await topology.WaitUntilConnectedAsync(sender, subscriber, bystander);

                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await atSubscriber.NextAsync(TestBus.Timeout));
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(0, atBystander.Count);
            }
        }

        [Fact]
        public async Task F2_SharedEvents_TravelOnThePublishersOwnExchange()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp sender = topology.Start(AppB, new string[0], AppBEvents, SharedEvents);
                LegacyApp subscriber = topology.Start(AppA, new[] { AppB }, AppAEvents, SharedEvents);
                Received<User> atSubscriber = subscriber.Listen<UserChanged, User>();
                await topology.WaitUntilConnectedAsync(sender, subscriber);
                WireCapture capture = await topology.CaptureAsync(AppB);

                sender.PublishRemote<UserChanged, User>(new User { Id = 7, Name = "shared", UpdatedAt = new DateTime(2026, 9, 26, 10, 15, 30, DateTimeKind.Utc) });

                User received = await atSubscriber.NextAsync(TestBus.Timeout);
                Assert.Equal("shared", received?.Name);
                CapturedMessage message = await capture.NextAsync(TestBus.Timeout);
                Assert.Equal(topology.Exchange(AppB), message?.Exchange);
            }
        }

        [Fact]
        public async Task A1_OwnExchange_IsDeclaredByItsApp_AsDurableTopic()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp app = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnectedAsync(app);

                Assert.Equal(200, await topology.DeclareExchangeAsync(app.ExchangeName, ExchangeType.Topic, durable: true));

                // 406 PRECONDITION_FAILED: the exchange exists with different arguments.
                Assert.Equal(406, await topology.DeclareExchangeAsync(app.ExchangeName, ExchangeType.Direct, durable: true));
                Assert.Equal(406, await topology.DeclareExchangeAsync(app.ExchangeName, ExchangeType.Topic, durable: false));
            }
        }

        [Fact]
        public async Task A16_SubscribedExchange_IsDeclaredByTheSubscriber_BeforeItsOwnerStarts()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp subscriber = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                await topology.WaitUntilConnectedAsync(subscriber);

                // AppA never started, yet its exchange exists, declared as a durable topic by AppB.
                Assert.Equal(200, await topology.ExchangeExistsAsync(topology.Exchange(AppA)));
                Assert.Equal(200, await topology.DeclareExchangeAsync(topology.Exchange(AppA), ExchangeType.Topic, durable: true));
            }
        }

        [Fact]
        public async Task F5_A3_A4_A5_PublishedMessage_OnTheWire()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp sender = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnectedAsync(sender);
                WireCapture capture = await topology.CaptureAsync(AppA);

                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                CapturedMessage message = await capture.NextAsync(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(sender.ExchangeName, message.Exchange);
                Assert.Equal(sender.ExchangeName, message.RoutingKey);                               // F5
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
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp app = topology.Start(AppA, new string[0], AppAEvents);
                await topology.WaitUntilConnectedAsync(app);

                Assert.Equal($"appa.{app.InstanceId}", app.QueueName);
                Assert.Equal(405, await topology.PassiveDeclareQueueAsync(app.QueueName));

                app.Dispose();
                ushort replyCode = 0;
                for (int attempt = 0; attempt < 50 && replyCode != 404; attempt++)
                {
                    replyCode = await topology.PassiveDeclareQueueAsync(app.QueueName);
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                Assert.Equal(404, replyCode);
            }
        }

        [Fact]
        public async Task F6_F7_Bindings_OnePerExchange_KeyedByTheExchangeName()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                // The only queue in this test: AppB's. So "routable" means "AppB has a binding for it".
                LegacyApp app = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                await topology.WaitUntilConnectedAsync(app);

                // F6: own exchange, keyed by its own name; event names aren't bound.
                Assert.True(await topology.IsRoutableAsync(AppB, topology.Exchange(AppB)));
                Assert.False(await topology.IsRoutableAsync(AppB, "AppB.Events.CustomerChanged"));

                // F7: subscribed exchange, keyed by its owner's name, nothing else.
                Assert.True(await topology.IsRoutableAsync(AppA, topology.Exchange(AppA)));
                Assert.False(await topology.IsRoutableAsync(AppA, "AppA.Events.OrderSaved"));
                Assert.False(await topology.IsRoutableAsync(AppA, "orders.eu.saved"));
            }
        }

        [Fact]
        public async Task A10_EventIsResolvedFromTheHeader_WithoutItTheMessageIsIgnored()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
            {
                LegacyApp app = topology.Start(AppB, new[] { AppA }, AppBEvents, AppAEvents);
                Received<Order> orders = app.Listen<OrderSaved, Order>();
                Received<Customer> customers = app.Listen<CustomerChanged, Customer>();
                await topology.WaitUntilConnectedAsync(app);
                byte[] body = GoldenFile.Read("compat-payload.json");

                // Every message on AppA's exchange carries AppA's name as its key: only the header says which event it is.
                string key = topology.Exchange(AppA);
                await topology.PublishRawAsync(AppA, key, new Dictionary<string, object> { [LegacyHeaders.EventType] = "AppA.Events.OrderSaved" }, body);
                AssertGolden(await orders.NextAsync(TestBus.Timeout));
                await topology.PublishRawAsync(AppA, key, new Dictionary<string, object> { [LegacyHeaders.EventType] = "AppB.Events.CustomerChanged" }, body);
                Assert.Equal(42, (await customers.NextAsync(TestBus.Timeout))?.Id);

                // No header, or an event AppB doesn't know: ignored.
                await topology.PublishRawAsync(AppA, key, null, body);
                await topology.PublishRawAsync(AppA, key, new Dictionary<string, object> { [LegacyHeaders.EventType] = "AppA.Events.NotAnEvent" }, body);

                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, orders.Count);
                Assert.Equal(1, customers.Count);
            }
        }

        [Fact]
        public async Task A12_FailingSubscriber_MessageIsAcked_AndNotRedelivered()
        {
            await using (LegacyTopology topology = await LegacyTopology.CreateAsync())
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
                await topology.WaitUntilConnectedAsync(sender, subscriber);

                Order failing = GoldenOrder();
                failing.Name = "fail";
                sender.PublishRemote<OrderSaved, Order>(failing);
                sender.PublishRemote<OrderSaved, Order>(GoldenOrder());

                AssertGolden(await received.NextAsync(TestBus.Timeout));
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
