using System;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Common.RabbitMQ.Tests.LegacyModel;
using Compat.Events;
using Messaging;
using Messaging.Prism;
using Microsoft.Extensions.DependencyInjection;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// Plain message classes through Prism's <see cref="IEventAggregator"/> (Messaging.Prism): the familiar Prism style for
    /// migrated WPF apps, against the frozen baseline Prism build over a real broker.
    /// </summary>
    public class EventAggregatorBridgeTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task IncomingMessages_AreRaisedAsMessageEvents()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                EventAggregator eventAggregator = new EventAggregator();
                Received<CompatMessage> received = Listen(eventAggregator);

                await using (TestHost host = await TestHost.Start(bus, "compat-wpf", messaging => messaging
                    .AddMessages(typeof(CompatMessage).Assembly)
                    .UseEventAggregator(eventAggregator)))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());

                    CompatMessage message = await received.Next(TestBus.Timeout);
                    Assert.Equal(42, message?.Id);
                    Assert.Equal("compat", message.Name);
                }
            }
        }

        [Fact]
        public async Task PublishRemote_LocalSubscribersOnce_OtherAppsReceive_NoEcho()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                EventAggregator eventAggregator = new EventAggregator();
                Received<CompatMessage> local = Listen(eventAggregator);

                await using (TestHost host = await TestHost.Start(bus, "compat-wpf", messaging => messaging
                    .AddMessages(typeof(CompatMessage).Assembly)
                    .Route<CompatMessage>().And()
                    .UseEventAggregator(eventAggregator)))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemote(GoldenMessage(), host.Services.GetRequiredService<IMessagePublisher>());

                    Assert.Equal(1, local.Count);
                    CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await baseline.Received.Next(TestBus.Timeout));

                    // The app's own copy comes back through its per-instance queue and is dropped.
                    await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                    Assert.Equal(1, local.Count);
                }
            }
        }

        [Fact]
        public async Task PublishRemoteTo_UsesTheRoutingKey()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                EventAggregator eventAggregator = new EventAggregator();

                await using (TestHost host = await TestHost.Start(bus, "compat-wpf", messaging => messaging
                    .Route<CompatMessage>().And()
                    .UseEventAggregator(eventAggregator)))
                {
                    await host.WaitUntilConnected();
                    WireCapture capture = await bus.Capture();

                    eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemoteTo("compat.eu", GoldenMessage(), host.Services.GetRequiredService<IMessagePublisher>());

                    CapturedMessage message = await capture.Next(TestBus.Timeout);
                    Assert.Equal("compat.eu", message?.RoutingKey);
                    Assert.Equal("Compat.Events.CompatEvent", message.Headers["event-type"]);
                }
            }
        }

        [Fact]
        public async Task HandlersAndTheAggregator_BothReceive()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                EventAggregator eventAggregator = new EventAggregator();
                Received<CompatMessage> received = Listen(eventAggregator);

                await using (TestHost host = await TestHost.Start(bus, "compat-wpf", messaging => messaging
                    .Handle<CompatMessage, CompatMessageHandler>().From(CompatBus.Name)
                    .UseEventAggregator(eventAggregator)))
                {
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());

                    Assert.NotNull(await host.Handled.Next(TestBus.Timeout));
                    Assert.NotNull(await received.Next(TestBus.Timeout));
                }
            }
        }

        private static Received<CompatMessage> Listen(EventAggregator eventAggregator)
        {
            Received<CompatMessage> received = new Received<CompatMessage>();
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(received.Add, ThreadOption.PublisherThread, keepSubscriberReferenceAlive: true);
            return received;
        }

        private static CompatMessage GoldenMessage()
        {
            CompatPayload payload = CompatPayloads.Golden();
            return new CompatMessage { Id = payload.Id, Name = payload.Name, UpdatedAt = payload.UpdatedAt };
        }
    }
}
