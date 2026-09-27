using System;
using System.Linq;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
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
    /// Messaging.Hosting for modern clients (ADR 0002, delivery plan step 8): routes without a bus, routing keys per route
    /// and per call, subscriptions in code, telemetry opt-in, receiving without handlers, and <see cref="MessagingClient"/>.
    /// </summary>
    public class HostingClientTests
    {
        private const string WireName = "Compat.Events.CompatEvent";

        [Fact]
        public async Task WithoutTelemetry_OnlyTheBaselineHeadersAreSent_AndTheOnlyBusIsTheRoute()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>()))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(GoldenMessage(), TestContext.Current.CancellationToken);

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(WireName, message.RoutingKey);
                Assert.Equal(new[] { "event-type", "source-id", "x-dotnet-pub-seq-no" }, message.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal));
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
            }
        }

        [Fact]
        public async Task Route_WithRoutingKey_ComputedFromTheMessage()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>().WithRoutingKey(m => $"compat.{m.Id}")))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                await host.Services.GetRequiredService<IMessagePublisher>().PublishAsync(GoldenMessage(), TestContext.Current.CancellationToken);

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.Equal("compat.42", message?.RoutingKey);
                Assert.Equal(WireName, message.Headers[WireHeaders.MessageType]);
            }
        }

        [Fact]
        public async Task PublishOptions_RoutingKey_OverridesTheRoute()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => messaging.Route<CompatMessage>().WithRoutingKey("route.key")))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();
                IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

                await publisher.PublishAsync(GoldenMessage(), new PublishOptions { RoutingKey = "call.key" }, TestContext.Current.CancellationToken);
                await publisher.PublishAsync(GoldenMessage(), TestContext.Current.CancellationToken);

                Assert.Equal("call.key", (await capture.Next(TestBus.Timeout))?.RoutingKey);
                Assert.Equal("route.key", (await capture.Next(TestBus.Timeout))?.RoutingKey);
            }
        }

        [Fact]
        public async Task AddMessages_ReceivesWithoutHandlers_ThroughASink()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                RecordingSink sink = new RecordingSink();

                await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging =>
                {
                    messaging.AddMessages(typeof(CompatMessage).Assembly);
                    messaging.Services.AddSingleton<IMessageSink>(sink);
                }))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());

                    CompatMessage received = Assert.IsType<CompatMessage>(await sink.Next(TestBus.Timeout));
                    Assert.Equal(42, received.Id);
                    Assert.Equal("compat", received.Name);
                }
            }
        }

        [Fact]
        public async Task MessagingClient_StartsWithoutAHost_AndPublishes()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                    .AddBus(CompatBus.Name, options => Configure(options, bus))
                    .Route<CompatMessage>())))
                {
                    await client.StartAsync(TestContext.Current.CancellationToken);

                    await client.Publisher.PublishAsync(GoldenMessage(), TestContext.Current.CancellationToken);

                    CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await baseline.Received.Next(TestBus.Timeout));
                }
            }
        }

        [Fact]
        public async Task RouteWithoutABus_WithSeveralBuses_FailsAtStartup()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("First", Unreachable)
                .AddBus("Second", Unreachable)
                .Route<CompatMessage>().And())))
            {
                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync(TestContext.Current.CancellationToken));
                Assert.Contains("has no bus", error.Message);
            }
        }

        [Fact]
        public void Subscribe_InCode_JoinsTheBusSubscriptions()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("Only", Unreachable)
                .Subscribe("AppA", "AppA.Events.#", "orders.*.saved"))))
            {
                RabbitMQBus bus = client.Services.GetRequiredKeyedService<RabbitMQBus>("Only");
                RabbitMQBusOptions options = client.Services.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get("Only");

                Assert.NotNull(bus);
                SubscriptionOptions subscription = Assert.Single(options.Subscriptions);
                Assert.Equal("AppA", subscription.Exchange);
                Assert.Equal(new[] { "AppA.Events.#", "orders.*.saved" }, subscription.RoutingKeys);
            }
        }

        private static void Configure(RabbitMQBusOptions options, TestBus bus)
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
