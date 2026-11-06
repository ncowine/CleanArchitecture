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
            await using (TestBus first = await TestBus.Create())
            await using (TestBus second = await TestBus.Create())
            {
                using (MessagingClient client = TwoBuses(first, second))
                {
                    await client.StartAsync(TestContext.Current.CancellationToken);
                    List<string> buses = Subscribe(client, new SubscribeOptions { Exchange = second.ExchangeName, RoutingKey = Key });

                    await AssertArrivesOnlyOn("Second", second, buses);
                }
            }
        }

        [Fact]
        public async Task TheBusOption_ChoosesTheBus()
        {
            await using (TestBus first = await TestBus.Create())
            await using (TestBus second = await TestBus.Create())
            {
                using (MessagingClient client = TwoBuses(first, second))
                {
                    await client.StartAsync(TestContext.Current.CancellationToken);
                    List<string> buses = Subscribe(client, new SubscribeOptions { Bus = "First", Exchange = second.ExchangeName, RoutingKey = Key });

                    await AssertArrivesOnlyOn("First", second, buses);
                }
            }
        }

        [Fact]
        public void AnExchangeOnSeveralBuses_NeedsTheBusOption()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("First", options => Unreachable(options, "first"))
                .AddBus("Second", options => Unreachable(options, "second"))
                .Subscribe(new SubscriptionOptions { Exchange = "OldApp" })
                .UseDefaultBus("First"))))
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
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("Only", options => Unreachable(options, "only"))
                .UseDefaultBus("Only"))))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => client.Services.GetRequiredService<IMessageSubscriber>()
                    .Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Bus = "Mistyped", RoutingKey = Key }));
                Assert.Contains("Mistyped", error.Message);
            }
        }

        /// <summary>Bus First owns <paramref name="first"/>'s exchange and is the default; bus Second owns <paramref name="second"/>'s.</summary>
        private static MessagingClient TwoBuses(TestBus first, TestBus second)
        {
            return MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("First", options => HostingClientTests.Configure(options, first))
                .AddBus("Second", options => HostingClientTests.Configure(options, second))
                .UseDefaultBus("First")));
        }

        /// <summary>Subscribes with <paramref name="options"/> and returns the buses each received message came on.</summary>
        private static List<string> Subscribe(MessagingClient client, SubscribeOptions options)
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

        private static async Task AssertArrivesOnlyOn(string busName, TestBus exchange, List<string> buses)
        {
            Assert.True(await exchange.WaitUntilRoutable(Key, true));

            Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = "Compat.Events.CompatEvent" };
            await exchange.PublishRaw(headers, GoldenFile.Read("compat-payload.json"), Key);
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
