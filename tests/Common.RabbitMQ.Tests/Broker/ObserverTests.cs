using System;
using System.Threading;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>The observer hook (ADR 0001, sections 1 and 5; delivery plan step 2).</summary>
    public class ObserverTests
    {
        /// <summary>How long to keep listening for a callback or message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task SeesPublishReceiveAndConnections()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver senderObserver = new RecordingObserver(context => context.Headers[WireHeaders.CorrelationId] = "corr-1");
                RecordingObserver receiverObserver = new RecordingObserver();
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-sender", senderObserver);
                CoreEndpoint receiver = bus.AddCoreEndpoint("compat-receiver", receiverObserver);
                await bus.WaitUntilConnected();

                sender.PublishRemote(CompatPayloads.Golden());
                int callerThread = Thread.CurrentThread.ManagedThreadId;

                ObservedEvent publishing = await senderObserver.WaitFor(nameof(IMessagingObserver.OnPublishing), TestBus.Timeout);
                Assert.NotNull(publishing);
                Assert.Equal(callerThread, publishing.ThreadId);
                Assert.Equal(CoreEndpoint.WireName, publishing.WireName);

                ObservedEvent published = await senderObserver.WaitFor(nameof(IMessagingObserver.OnPublished), TestBus.Timeout);
                Assert.NotNull(published);
                Assert.Equal(publishing.MessageId, published.MessageId);

                ObservedEvent handled = await receiverObserver.WaitFor(nameof(IMessagingObserver.OnHandled), TestBus.Timeout);
                Assert.NotNull(handled);
                Assert.Equal(publishing.MessageId, handled.MessageId);
                Assert.Equal("corr-1", handled.CorrelationId);
                Assert.Equal(CoreEndpoint.WireName, handled.WireName);
                Assert.Equal(1, receiverObserver.Count(nameof(IMessagingObserver.OnReceived)));
                Assert.Equal(1, receiver.Received.Count);

                foreach (RecordingObserver observer in new[] { senderObserver, receiverObserver })
                {
                    foreach (ConnectionRole role in new[] { ConnectionRole.Consumer, ConnectionRole.Publisher })
                    {
                        ObservedEvent connected = await observer.WaitFor(
                            nameof(IMessagingObserver.OnConnectionChanged),
                            TestBus.Timeout,
                            e => e.Connection.Role == role && e.Connection.Status == ConnectionStatus.Connected);
                        Assert.NotNull(connected);
                        Assert.Equal(CompatBus.Name, connected.Connection.BusName);
                    }
                }

                // The sender's own copy is dropped before the observer sees it.
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(0, senderObserver.Count(nameof(IMessagingObserver.OnReceived)));
            }
        }

        [Fact]
        public async Task SeesHandlingFailure_AndTheMessageIsStillAcked()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver observer = new RecordingObserver();
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-sender", null);
                CoreEndpoint receiver = bus.AddCoreEndpoint("compat-receiver", observer);
                InvalidOperationException failure = new InvalidOperationException("handler failed");
                receiver.HandlerException = failure;
                await bus.WaitUntilConnected();

                sender.PublishRemote(CompatPayloads.Golden());

                ObservedEvent failed = await observer.WaitFor(nameof(IMessagingObserver.OnHandlingFailed), TestBus.Timeout);
                Assert.NotNull(failed);
                Assert.Same(failure, failed.Exception);
                Assert.Equal(0, observer.Count(nameof(IMessagingObserver.OnHandled)));

                // Acked, so never redelivered (current legacy behaviour, ADR 0001 section 3).
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(1, observer.Count(nameof(IMessagingObserver.OnReceived)));
            }
        }

        [Fact]
        public async Task ThrowingObserver_DoesNotAffectDelivery()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver senderObserver = new RecordingObserver(throwing: true);
                RecordingObserver receiverObserver = new RecordingObserver(throwing: true);
                CoreEndpoint sender = bus.AddCoreEndpoint("compat-sender", senderObserver);
                CoreEndpoint receiver = bus.AddCoreEndpoint("compat-receiver", receiverObserver);
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                CompatPayload payload = CompatPayloads.Golden();
                sender.PublishRemote(payload);

                CompatibilityMatrixTests.AssertPayload(payload, await receiver.Received.Next(TestBus.Timeout));
                CompatibilityMatrixTests.AssertPayload(payload, await baseline.Received.Next(TestBus.Timeout));
                Assert.NotNull(await receiverObserver.WaitFor(nameof(IMessagingObserver.OnHandled), TestBus.Timeout));
                Assert.NotNull(await senderObserver.WaitFor(nameof(IMessagingObserver.OnPublished), TestBus.Timeout));
            }
        }

        /// <summary>Needs no broker: nothing listens on the port.</summary>
        [Fact]
        public async Task SeesFailedConnections()
        {
            RecordingObserver observer = new RecordingObserver();
            RabbitMQBusOptions options = new RabbitMQBusOptions
            {
                BusName = CompatBus.Name,
                HostName = "127.0.0.1",
                Port = 1,
                ExchangeName = "compat.unreachable",
                ClientName = "compat-unreachable",
                ReconnectDelay = TimeSpan.FromSeconds(1),
            };

            using (RabbitMQBus bus = new RabbitMQBus(options, TestJsonSerializer.Instance, new NullDispatcher(), observer))
            {
                bus.Start(null);

                foreach (ConnectionRole role in new[] { ConnectionRole.Consumer, ConnectionRole.Publisher })
                {
                    ObservedEvent failed = await observer.WaitFor(
                        nameof(IMessagingObserver.OnConnectionChanged),
                        TestBus.Timeout,
                        e => e.Connection.Role == role && e.Connection.Status == ConnectionStatus.Failed);
                    Assert.NotNull(failed);
                    Assert.NotNull(failed.Connection.Error);
                }
            }
        }
    }
}
