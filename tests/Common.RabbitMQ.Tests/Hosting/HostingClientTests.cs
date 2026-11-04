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
    /// Messaging.Hosting for modern clients (ADR 0002, delivery plan step 8): publishing to the only bus, routing keys per
    /// call, subscriptions in code, telemetry opt-in, receiving without handlers, and <see cref="MessagingClient"/>.
    /// </summary>
    public class HostingClientTests
    {
        private const string WireName = "Compat.Events.CompatEvent";

        [Fact]
        public async Task WithoutTelemetry_OnlyTheBaselineHeadersAreSent_ToTheOnlyBus()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => { }))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();

                host.Services.GetRequiredService<IMessagePublisher>().Publish(GoldenMessage());

                CapturedMessage message = await capture.Next(TestBus.Timeout);
                Assert.NotNull(message);
                Assert.Equal(WireName, message.RoutingKey);
                Assert.Equal(new[] { "event-type", "source-id", "x-dotnet-pub-seq-no" }, message.Headers.Keys.OrderBy(k => k, StringComparer.Ordinal));
                Assert.Equal(GoldenFile.Read("compat-payload.json"), message.Body);
            }
        }

        [Fact]
        public async Task PublishOptions_RoutingKey_OverridesTheWireName()
        {
            await using (TestBus bus = await TestBus.Create())
            await using (TestHost host = await TestHost.Start(bus, "compat-client", messaging => { }))
            {
                await host.WaitUntilConnected();
                WireCapture capture = await bus.Capture();
                IMessagePublisher publisher = host.Services.GetRequiredService<IMessagePublisher>();

                publisher.Publish(GoldenMessage(), new PublishOptions { RoutingKey = "call.key" });
                publisher.Publish(GoldenMessage());

                Assert.Equal("call.key", (await capture.Next(TestBus.Timeout))?.RoutingKey);
                Assert.Equal(WireName, (await capture.Next(TestBus.Timeout))?.RoutingKey);
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
                    .AddBus(CompatBus.Name, options => Configure(options, bus)))))
                {
                    await client.StartAsync(TestContext.Current.CancellationToken);

                    client.Publisher.Publish(GoldenMessage());

                    CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await baseline.Received.Next(TestBus.Timeout));
                }
            }
        }

        [Fact]
        public void Publishing_WithSeveralBuses_AndNoDefault_Throws()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("First", Unreachable)
                .AddBus("Second", Unreachable))))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => client.Publisher.Publish(GoldenMessage()));
                Assert.Contains("no default", error.Message);
            }
        }

        [Fact]
        public async Task Publishing_GoesToTheDefaultBus_UnlessTheCallNamesAnother()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("First", Unreachable)
                .AddBus("Second", Unreachable)
                .UseDefaultBus("Second"))))
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
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging.AddBus("Only", Unreachable))))
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    client.Publisher.Publish(GoldenMessage(), new PublishOptions { Bus = "Mistyped" }));
                Assert.Contains("Mistyped", error.Message);
            }
        }

        [Fact]
        public void UseDefaultBus_Twice_Throws()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                    .AddBus("First", Unreachable)
                    .AddBus("Second", Unreachable)
                    .UseDefaultBus("First")
                    .UseDefaultBus("Second"))));
            Assert.Contains("only one", error.Message);
        }

        [Fact]
        public void UseDefaultBus_WithNoValue_LeavesTheOnlyBusAsTheDefault()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("Only", Unreachable)
                .UseDefaultBus(null))))
            {
                client.Publisher.Publish(GoldenMessage());

                Assert.Equal(1, client.Services.GetRequiredKeyedService<RabbitMQBus>("Only").OutstandingCount);
            }
        }

        [Fact]
        public async Task UnknownDefaultBus_FailsAtStartup()
        {
            using (MessagingClient client = MessagingClient.Create(services => services.AddMessaging(messaging => messaging
                .AddBus("Only", Unreachable)
                .UseDefaultBus("Mistyped"))))
            {
                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync(TestContext.Current.CancellationToken));
                Assert.Contains("Mistyped", error.Message);
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
