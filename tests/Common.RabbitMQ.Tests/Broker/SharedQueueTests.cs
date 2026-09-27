using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Golden;
using Compat.Events;
using Messaging;
using Messaging.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// Server-side shared quorum queues (ADR 0001, section 6; delivery plan step 3b): competing consumers, durability,
    /// in-process retries and dead-lettering. Legacy clients keep their per-instance queues (the other broker tests).
    /// </summary>
    public class SharedQueueTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task CompetingConsumers_EachMessageIsHandledOnce()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                string service = NewServiceName();
                CoreEndpoint first = bus.AddCoreEndpoint(service, null, Shared(prefetch: 1));
                CoreEndpoint second = bus.AddCoreEndpoint(service, null, Shared(prefetch: 1));
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                const int count = 20;
                for (int id = 1; id <= count; id++)
                {
                    sender.PublishRemote(new CompatPayload { Id = id, Name = "competing", UpdatedAt = DateTime.UtcNow });
                }

                await WaitFor(() => first.Received.Count + second.Received.Count >= count);
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);

                List<int> ids = (await Drain(first.Received)).Concat(await Drain(second.Received)).Select(p => p.Id).ToList();
                Assert.Equal(Enumerable.Range(1, count), ids.OrderBy(id => id));
                Assert.True(first.Received.Count > 0 && second.Received.Count > 0, $"Work was not shared: {first.Received.Count} and {second.Received.Count}.");
            }
        }

        [Fact]
        public async Task Messages_WaitInTheQueue_WhileNoInstanceIsRunning()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                string service = NewServiceName();
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                CoreEndpoint before = bus.AddCoreEndpoint(service, null, Shared());
                await bus.WaitUntilConnected(sender, before);

                // The queue and its binding exist now; stop the only instance.
                before.Dispose();
                sender.PublishRemote(CompatPayloads.Golden());
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);

                CoreEndpoint after = bus.AddCoreEndpoint(service, null, Shared());
                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await after.Received.Next(TestBus.Timeout));
                Assert.Equal(0, before.Received.Count);
            }
        }

        [Fact]
        public async Task OwnMessages_AreHandled()
        {
            // No echo drop on shared queues: a service that subscribes to what it publishes handles it (for example,
            // two modules of one process), whichever instance picks it up.
            await using (TestBus bus = await TestBus.Create())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), null, Shared());
                await bus.WaitUntilConnected();

                endpoint.PublishRemote(CompatPayloads.Golden());

                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await endpoint.Received.Next(TestBus.Timeout));
            }
        }

        [Fact]
        public async Task FailingHandler_IsRetried_UntilItSucceeds()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver observer = new RecordingObserver();
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), observer, Shared(maxAttempts: 3));
                endpoint.HandlerException = new InvalidOperationException("transient");
                endpoint.FailTimes = 2;
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                sender.PublishRemote(CompatPayloads.Golden());

                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await endpoint.Received.Next(TestBus.Timeout));
                Assert.NotNull(await observer.WaitFor(nameof(IMessagingObserver.OnHandled), TestBus.Timeout));
                Assert.Equal(3, endpoint.Attempts);
                Assert.Equal(
                    new FailedMessageAction?[] { FailedMessageAction.Retrying, FailedMessageAction.Retrying },
                    observer.Events.Where(e => e.Callback == nameof(IMessagingObserver.OnHandlingFailed)).Select(e => e.Action));
                Assert.Null(await bus.Get(endpoint.Bus.DeadLetterQueueName));
            }
        }

        [Fact]
        public async Task FailingHandler_IsDeadLettered_AfterMaxAttempts()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver observer = new RecordingObserver();
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), observer, Shared(maxAttempts: 3));
                endpoint.HandlerException = new InvalidOperationException("permanent");
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                await bus.WaitUntilConnected();

                sender.PublishRemote(CompatPayloads.Golden());

                ObservedEvent deadLettered = await observer.WaitFor(
                    nameof(IMessagingObserver.OnHandlingFailed), TestBus.Timeout, e => e.Action == FailedMessageAction.DeadLettered);
                Assert.NotNull(deadLettered);
                Assert.Same(endpoint.HandlerException, deadLettered.Exception);
                Assert.Equal(3, endpoint.Attempts);

                CapturedMessage dead = await WaitForDeadLetter(bus, endpoint);
                Assert.Equal(GoldenFile.Read("compat-payload.json"), dead.Body);
                Assert.Equal(CoreEndpoint.WireName, dead.Headers[WireHeaders.MessageType]);
                Assert.Equal(sender.InstanceId, dead.Headers[WireHeaders.SourceId]);
                Assert.Equal("rejected", dead.Headers["x-first-death-reason"]);

                // Settled once: never redelivered.
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Equal(3, endpoint.Attempts);
            }
        }

        [Fact]
        public async Task UndeserializableMessage_IsDeadLettered_WithoutRetrying()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                RecordingObserver observer = new RecordingObserver();
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), observer, Shared(maxAttempts: 3));
                await bus.WaitUntilConnected();

                byte[] poison = Encoding.UTF8.GetBytes("not json");
                await bus.PublishRaw(CoreEndpoint.WireName, null, poison);

                ObservedEvent failed = await observer.WaitFor(nameof(IMessagingObserver.OnHandlingFailed), TestBus.Timeout);
                Assert.NotNull(failed);
                Assert.Equal(FailedMessageAction.DeadLettered, failed.Action);
                Assert.Equal(0, endpoint.Attempts);
                Assert.Equal(poison, (await WaitForDeadLetter(bus, endpoint)).Body);
            }
        }

        [Fact]
        public async Task UnknownMessage_IsDeadLettered()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), null, Shared());
                await bus.WaitUntilConnected();

                // Routed here by its routing key, but the event-type header names a message this bus doesn't handle.
                Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = "Something.Else" };
                await bus.PublishRaw(CoreEndpoint.WireName, headers, GoldenFile.Read("compat-payload.json"));

                CapturedMessage dead = await WaitForDeadLetter(bus, endpoint);
                Assert.Equal("Something.Else", dead.Headers[WireHeaders.MessageType]);
                Assert.Equal(0, endpoint.Attempts);
            }
        }

        [Fact]
        public async Task MessageThatKeepsCrashingTheConsumer_IsDeadLettered_ByTheDeliveryLimit()
        {
            // RabbitMQ 4.x counts deliveries lost with a connection (a crashed process), not requeues. The delivery
            // limit is the guard for a message that takes the whole process down every time it is handled.
            await using (TestBus bus = await TestBus.Create())
            {
                CoreEndpoint declaring = bus.AddCoreEndpoint(NewServiceName(), null, options =>
                {
                    Shared()(options);
                    options.DeliveryLimit = 2;
                });
                await bus.WaitUntilConnected(declaring);
                declaring.Dispose();

                await bus.PublishRaw(CoreEndpoint.WireName, null, GoldenFile.Read("compat-payload.json"));

                // Keep crashing until the broker stops redelivering; dead-lettering happens asynchronously.
                int crashes = 0;
                while (crashes < 5 && await CrashWhileHandling(bus, declaring.Bus.QueueName))
                {
                    crashes++;
                }

                CapturedMessage dead = await WaitForDeadLetter(bus, declaring);
                Assert.Equal("delivery_limit", dead.Headers["x-first-death-reason"]);
                Assert.Equal(GoldenFile.Read("compat-payload.json"), dead.Body);
                Assert.InRange(crashes, 1, 3);
            }
        }

        /// <summary>Takes one delivery and drops the connection without settling it, as a crashed process would.</summary>
        /// <returns>False when nothing was delivered within a few seconds.</returns>
        private static async Task<bool> CrashWhileHandling(TestBus bus, string queueName)
        {
            using (IConnection connection = await bus.Broker.CreateConnectionFactory().CreateConnectionAsync("compat-crashing"))
            using (IChannel channel = await connection.CreateChannelAsync())
            {
                TaskCompletionSource<bool> delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (sender, args) =>
                {
                    delivered.TrySetResult(true);
                    return Task.CompletedTask;
                };

                await channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer);
                bool wasDelivered = await Task.WhenAny(delivered.Task, Task.Delay(TimeSpan.FromSeconds(3))) == delivered.Task;

                // Abort: no ack, no clean channel close.
                await connection.AbortAsync();
                return wasDelivered;
            }
        }

        private static Action<RabbitMQBusOptions> Shared(ushort prefetch = 50, int maxAttempts = 3)
        {
            return options =>
            {
                options.QueueMode = QueueMode.Shared;
                options.PrefetchCount = prefetch;
                options.MaxAttempts = maxAttempts;
            };
        }

        /// <summary>Unique per test: shared queues are named after the service and outlive their consumers.</summary>
        private static string NewServiceName()
        {
            return "svc-" + Guid.NewGuid().ToString("N");
        }

        private static async Task<CapturedMessage> WaitForDeadLetter(TestBus bus, CoreEndpoint endpoint)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                CapturedMessage dead = await bus.Get(endpoint.Bus.DeadLetterQueueName);
                if (dead != null)
                {
                    return dead;
                }

                Assert.True(stopwatch.Elapsed < TestBus.Timeout, "Nothing arrived in the dead-letter queue.");
                await Task.Delay(50);
            }
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(stopwatch.Elapsed < TestBus.Timeout, "Timed out.");
                await Task.Delay(50);
            }
        }

        private static async Task<List<CompatPayload>> Drain(ReceivedPayloads received)
        {
            List<CompatPayload> payloads = new List<CompatPayload>();
            for (CompatPayload payload = await received.Next(TimeSpan.Zero); payload != null; payload = await received.Next(TimeSpan.Zero))
            {
                payloads.Add(payload);
            }

            return payloads;
        }
    }
}
