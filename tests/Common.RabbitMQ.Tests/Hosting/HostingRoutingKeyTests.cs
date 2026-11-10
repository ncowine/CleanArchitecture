using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Common.RabbitMQ.Tests.LegacyModel;
using Compat.Events;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// Subscribing with <see cref="SubscribeOptions"/> binds a routing key; the last subscription with it unbinds it. The
    /// exchange-name binding stays whatever happens.
    /// </summary>
    public class HostingRoutingKeyTests
    {
        private const string Key = "compat.keyed";

        [Fact]
        public async Task SubscribingWithAKey_Receives_AndDisposingUnbinds()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => { }))
            {
                await host.WaitUntilConnected();
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                Assert.False(await bus.IsRoutable(Key));

                Received<CompatMessage> received = new Received<CompatMessage>();
                string receivedKey = null;
                IDisposable subscription = subscriber.Subscribe<CompatMessage>((message, context, token) =>
                {
                    receivedKey = context.RoutingKey;
                    received.Add(message);
                    return Task.CompletedTask;
                }, new SubscribeOptions { RoutingKey = Key });
                Assert.True(await bus.WaitUntilRoutable(Key, true));

                await PublishGolden(bus, Key);
                Assert.Equal(42, (await received.Next(TestBus.Timeout))?.Id);
                Assert.Equal(Key, receivedKey);

                subscription.Dispose();
                Assert.True(await bus.WaitUntilRoutable(Key, false));
                Assert.True(await bus.IsRoutable(bus.ExchangeName));
            }
        }

        [Fact]
        public async Task TheBinding_StaysUntilTheLastSubscriptionWithTheKeyGoes()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => { }))
            {
                await host.WaitUntilConnected();
                IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();

                // Two message types sharing one key: one binding.
                IDisposable first = subscriber.Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { RoutingKey = Key });
                IDisposable second = subscriber.Subscribe<KeyedMessage>(_ => { }, options: new SubscribeOptions { RoutingKey = Key });
                Assert.True(await bus.WaitUntilRoutable(Key, true));

                first.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
                Assert.True(await bus.IsRoutable(Key));

                second.Dispose();
                Assert.True(await bus.WaitUntilRoutable(Key, false));
            }
        }

        [Fact]
        public async Task SubscribingBeforeStart_BindsWhenTheBusConnects()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                    .AddBus(CompatBus.Name, options => HostingClientTests.Configure(options, bus))
                    .UseDefaultBus(CompatBus.Name))))
                {
                    client.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { RoutingKey = Key });
                    Assert.False(await bus.IsRoutable(Key));

                    await client.StartAsync(TestContext.Current.CancellationToken);

                    Assert.True(await bus.WaitUntilRoutable(Key, true));
                }
            }
        }

        [Fact]
        public async Task AHandlersRoutingKey_IsBoundAtStartup()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => messaging.AddHandlers(typeof(KeyedMessageHandler).Assembly)))
            {
                Assert.True(await bus.WaitUntilRoutable(KeyedMessageHandler.Key, true));
            }
        }

        [Fact]
        public async Task AKeyOnAMissingExchange_IsSkipped_AndTheBusKeepsReceiving()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");

                await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => { }))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();
                    IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();

                    Received<CompatMessage> received = new Received<CompatMessage>();
                    subscriber.Subscribe<CompatMessage>(received.Add, options: new SubscribeOptions { Exchange = "compat.no-such-exchange", RoutingKey = Key });
                    await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

                    legacy.PublishRemote(CompatPayloads.Golden());
                    Assert.Equal(42, (await received.Next(TestBus.Timeout))?.Id);
                    Assert.True(host.Bus.IsConsumerConnected);
                }
            }
        }

        [Fact]
        public void SubscribeOptions_WithoutAKey_IsRefused()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("Only", options => { options.ExchangeName = "only"; options.HostName = "127.0.0.1"; options.Port = 1; })
                .UseDefaultBus("Only"))))
            {
                IMessageSubscriber subscriber = client.Services.GetRequiredService<IMessageSubscriber>();

                Assert.Throws<ArgumentException>(() => subscriber.Subscribe<CompatMessage>(_ => { }, options: new SubscribeOptions { Exchange = "x" }));
            }
        }

        private static Task PublishGolden(TestBus bus, string routingKey)
        {
            Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = "Compat.Events.CompatEvent" };
            return bus.PublishRaw(headers, GoldenFile.Read("compat-payload.json"), routingKey);
        }
    }
}
