using System;
using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Common.RabbitMQ.Tests.LegacyModel;
using Compat.Events;
using Messaging;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// Messaging.Hosting for modern clients (ADR 0002, delivery plan step 8): publishing to the only bus, routing keys per
    /// call, and the default bus.
    /// </summary>
    public class HostingClientTests
    {
        [Fact]
        public async Task OnlyTheBaselineHeadersAreSent_ToTheOnlyBus()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            await using (TestHost host = await TestHost.StartAsync(bus, "compat-client"))
            {
                await host.WaitUntilConnectedAsync();
                WireCapture capture = await bus.CaptureAsync();

                host.Services.GetRequiredService<IMessagePublisher>().Publish(GoldenMessage());

                CapturedMessage message = await capture.NextAsync(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(bus.ExchangeName, message.RoutingKey);
                Assert.Equal(new[] { "event-type", "source-id", "x-dotnet-pub-seq-no" }, message.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal));
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
            }
        }

        [Fact]
        public async Task PublishOptions_RoutingKey_OverridesTheExchangeName()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            await using (TestHost host = await TestHost.StartAsync(bus, "compat-client"))
            {
                await host.WaitUntilConnectedAsync();
                WireCapture capture = await bus.CaptureAsync();
                IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

                publisher.Publish(GoldenMessage(), new PublishOptions { RoutingKey = "call.key" });
                publisher.Publish(GoldenMessage());

                Assert.Equal("call.key", (await capture.NextAsync(TestBus.Timeout))?.RoutingKey);
                Assert.Equal(bus.ExchangeName, (await capture.NextAsync(TestBus.Timeout))?.RoutingKey);
            }
        }


        [Fact]
        public async Task Publishes_ToALegacyApp()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");
                await bus.WaitUntilConnectedAsync();

                using (TestClient client = TestClient.Create(services => services.AddTestMessaging(CompatBus.Name, (CompatBus.Name, options => Configure(options, bus)))))
                {
                    await client.StartAsync(TestContext.Current.CancellationToken);

                    client.Publisher.Publish(GoldenMessage());

                    CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await legacy.Received.NextAsync(TestBus.Timeout));
                }
            }
        }

        [Fact]
        public async Task Subscribing_BeforeTheClientStarts_BindsWhenItConnects()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                ICompatEndpoint legacy = bus.AddEndpoint(CompatBuild.Current, "compat-legacy");
                await bus.WaitUntilConnectedAsync();

                using (TestClient client = TestClient.Create(services => services.AddTestMessaging(CompatBus.Name, (CompatBus.Name, options => Configure(options, bus)))))
                {
                    Received<CompatMessage> received = new Received<CompatMessage>();
                    client.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(received.Add);

                    await client.StartAsync(TestContext.Current.CancellationToken);
                    RabbitMQBus busClient = client.Services.GetRequiredKeyedService<RabbitMQBus>(CompatBus.Name);
                    for (int i = 0; i < 100 && !busClient.IsConsumerConnected; i++)
                    {
                        await Task.Delay(100, TestContext.Current.CancellationToken);
                    }

                    legacy.PublishRemote(CompatPayloads.Golden());

                    Assert.Equal(42, (await received.NextAsync(TestBus.Timeout))?.Id);
                }
            }
        }

        [Fact]
        public async Task Publishing_GoesToTheDefaultBus_UnlessTheCallNamesAnother()
        {
            using (TestClient client = TestClient.Create(services => services.AddTestMessaging("Second", ("First", Unreachable), ("Second", Unreachable))))
            {
                await client.StartAsync(TestContext.Current.CancellationToken);
                RabbitMQBus first = client.Services.GetRequiredKeyedService<RabbitMQBus>("First");
                RabbitMQBus second = client.Services.GetRequiredKeyedService<RabbitMQBus>("Second");

                client.Publisher.Publish(GoldenMessage());
                Assert.Equal((0, 1), (first.OutstandingCount, second.OutstandingCount));

                client.Publisher.Publish(GoldenMessage(), new PublishOptions { Bus = "First" });
                Assert.Equal((1, 1), (first.OutstandingCount, second.OutstandingCount));
            }
        }

        [Fact]
        public void Publishing_ToAnUnknownBus_Throws()
        {
            using (TestClient client = TestClient.Create(services => services.AddTestMessaging("Only", ("Only", Unreachable))))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    client.Publisher.Publish(GoldenMessage(), new PublishOptions { Bus = "Mistyped" }));
                Assert.Contains("Mistyped", error.Message);
            }
        }


        internal static void Configure(RabbitMQBusOptions options, TestBus bus)
        {
            options.HostName = bus.Broker.HostName;
            options.Port = bus.Broker.Port;
            options.VirtualHost = bus.Broker.VirtualHost;
            options.UserName = bus.Broker.UserName;
            options.Password = bus.Broker.Password;
            options.ExchangeName = bus.ExchangeName;
            options.ClientName = "compat-client";
            options.ReconnectDelay = TimeSpan.FromSeconds(1);
            options.OutstandingPollInterval = TimeSpan.FromMilliseconds(20);
        }

        /// <summary>Nothing listens on port 1: the bus never connects, which these tests don't need.</summary>
        private static void Unreachable(RabbitMQBusOptions options)
        {
            options.HostName = "127.0.0.1";
            options.Port = 1;
            options.ExchangeName = "compat.unreachable";
            options.ClientName = "compat-unreachable";
        }

        private static CompatMessage GoldenMessage()
        {
            CompatPayload payload = CompatPayloads.Golden();
            return new CompatMessage { Id = payload.Id, Name = payload.Name, UpdatedAt = payload.UpdatedAt };
        }
    }
}
