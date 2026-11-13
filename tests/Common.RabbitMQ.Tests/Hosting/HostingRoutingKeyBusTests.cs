using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Common.RabbitMQ.Tests.LegacyModel;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// With several buses, a routing key goes on <see cref="SubscribeOptions.Bus"/>, else on the one bus that uses the
    /// exchange, else on the default bus. Each received message says which bus brought it.
    /// </summary>
    public class HostingRoutingKeyBusTests
    {
        private const string Key = "compat.keyed";

        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task WithoutABus_TheKeyGoesOnTheBusThatUsesTheExchange()
        {
            await using (TestBus first = await TestBus.CreateAsync())
            await using (TestBus second = await TestBus.CreateAsync())
            {
                using (TestClient client = TwoBuses(first, second))
                {
                    await client.StartAsync(TestContext.Current.CancellationToken);
                    List<string> buses = Subscribe(client, new SubscribeOptions { Exchange = second.ExchangeName, RoutingKey = Key });

                    await AssertArrivesOnlyOnAsync("Second", second, buses);
                }
            }
        }

        [Fact]
        public void TheBusOption_WithAnExchangeThatBusDoesNotUse_IsRefused()
        {
            using (TestClient client = TestClient.Create(services => services.AddTestMessaging("First", ("First", options => Unreachable(options, "first")), ("Second", options => Unreachable(options, "second")))))
            {
                IMessageSubscriber subscriber = client.Services.GetRequiredService<IMessageSubscriber>();

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    subscriber.Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Bus = "First", Exchange = "second", RoutingKey = Key }));
                Assert.Contains("'second'", error.Message);
                Assert.Contains("'First'", error.Message);
            }
        }

        [Fact]
        public void AnExchangeNoBusUses_IsRefused_AndNothingIsLeftReceiving()
        {
            using (TestClient client = TestClient.Create(services => services.AddTestMessaging("Only", ("Only", options => Unreachable(options, "only")))))
            {
                IMessageSubscriber subscriber = client.Services.GetRequiredService<IMessageSubscriber>();

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    subscriber.Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Exchange = "Mistyped", RoutingKey = Key }));
                Assert.Contains("no bus uses it", error.Message);

                // A later subscription to a twin class with the same wire name is still allowed: the refused one left nothing behind.
                subscriber.Subscribe<Employees.Contracts.CompatMessageTwin>(_ => { }).Dispose();
            }
        }

        [Fact]
        public void AnExchangeOnSeveralBuses_NeedsTheBusOption()
        {
            using (TestClient client = TestClient.Create(services => services.AddTestMessaging("First", ("First", options => { Unreachable(options, "first"); options.Subscriptions.Add(new SubscriptionOptions { Exchange = "OldApp" }); }), ("Second", options => { Unreachable(options, "second"); options.Subscriptions.Add(new SubscriptionOptions { Exchange = "OldApp" }); }))))
            {
                IMessageSubscriber subscriber = client.Services.GetRequiredService<IMessageSubscriber>();

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    subscriber.Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Exchange = "OldApp", RoutingKey = Key }));
                Assert.Contains("SubscribeOptions.Bus", error.Message);

                subscriber.Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Bus = "Second", Exchange = "OldApp", RoutingKey = Key }).Dispose();
            }
        }

        [Fact]
        public void AnUnknownBus_IsRefused()
        {
            using (TestClient client = TestClient.Create(services => services.AddTestMessaging("Only", ("Only", options => Unreachable(options, "only")))))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => client.Services.GetRequiredService<IMessageSubscriber>()
                    .Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Bus = "Mistyped", RoutingKey = Key }));
                Assert.Contains("Mistyped", error.Message);
            }
        }

        /// <summary>Bus First owns <paramref name="first"/>'s exchange and is the default; bus Second owns <paramref name="second"/>'s.</summary>
        private static TestClient TwoBuses(TestBus first, TestBus second)
        {
            return TestClient.Create(services => services.AddTestMessaging("First", ("First", options => HostingClientTests.Configure(options, first)), ("Second", options => HostingClientTests.Configure(options, second))));
        }

        /// <summary>Subscribes with <paramref name="options"/> and returns the buses each received message came on.</summary>
        private static List<string> Subscribe(TestClient client, SubscribeOptions options)
        {
            List<string> buses = new List<string>();
            client.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>((message, context, token) =>
            {
                lock (buses)
                {
                    buses.Add(context.BusName);
                }

                return Task.CompletedTask;
            }, options);
            return buses;
        }

        private static async Task AssertArrivesOnlyOnAsync(string busName, TestBus exchange, List<string> buses)
        {
            Assert.True(await exchange.WaitUntilRoutableAsync(Key, true));

            Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = "Compat.Events.CompatEvent" };
            await exchange.PublishRawAsync(headers, GoldenFile.Read("compat-payload.json"), Key);
            await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);

            lock (buses)
            {
                Assert.Equal(new[] { busName }, buses);
            }
        }

        private static void Unreachable(RabbitMQBusOptions options, string exchangeName)
        {
            options.HostName = "127.0.0.1";
            options.Port = 1;
            options.ExchangeName = exchangeName;
            options.ClientName = "compat-unreachable";
        }
    }
}
