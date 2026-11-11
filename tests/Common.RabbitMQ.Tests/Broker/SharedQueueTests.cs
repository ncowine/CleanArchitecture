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
using Microsoft.Extensions.Logging;
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
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                string service = NewServiceName();
                CoreEndpoint first = bus.AddCoreEndpoint(service, Shared(prefetch: 1));
                CoreEndpoint second = bus.AddCoreEndpoint(service, Shared(prefetch: 1));
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                await bus.WaitUntilConnectedAsync();

                const int count = 20;
                for (int id = 1; id <= count; id++)
                {
                    sender.PublishRemote(new CompatPayload { Id = id, Name = "competing", UpdatedAt = DateTime.UtcNow });
                }

                await WaitForAsync(() => first.Received.Count + second.Received.Count >= count);
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);

                List<int> ids = (await DrainAsync(first.Received)).Concat(await DrainAsync(second.Received)).Select(p => p.Id).ToList();
                Assert.Equal(Enumerable.Range(1, count), ids.OrderBy(id => id));
                Assert.True(first.Received.Count > 0 && second.Received.Count > 0, $"Work was not shared: {first.Received.Count} and {second.Received.Count}.");
            }
        }

        [Fact]
        public async Task Messages_WaitInTheQueue_WhileNoInstanceIsRunning()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                string service = NewServiceName();
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                CoreEndpoint before = bus.AddCoreEndpoint(service, Shared());
                await bus.WaitUntilConnectedAsync(sender, before);

                // The queue and its binding exist now; stop the only instance.
                before.Dispose();
                sender.PublishRemote(CompatPayloads.Golden());
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);

                CoreEndpoint after = bus.AddCoreEndpoint(service, Shared());
                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await after.Received.NextAsync(TestBus.Timeout));
                Assert.Equal(0, before.Received.Count);
            }
        }

        [Fact]
        public async Task OwnMessages_AreHandled()
        {
            // No echo drop on shared queues: a service that subscribes to what it publishes handles it (for example,
            // two modules of one process), whichever instance picks it up.
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), Shared());
                await bus.WaitUntilConnectedAsync();

                endpoint.PublishRemote(CompatPayloads.Golden());

                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await endpoint.Received.NextAsync(TestBus.Timeout));
            }
        }

        [Fact]
        public async Task FailingHandler_IsRetried_UntilItSucceeds()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), Shared(maxAttempts: 3));
                endpoint.HandlerException = new InvalidOperationException("transient");
                endpoint.FailTimes = 2;
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                await bus.WaitUntilConnectedAsync();

                sender.PublishRemote(CompatPayloads.Golden());

                CompatibilityMatrixTests.AssertPayload(CompatPayloads.Golden(), await endpoint.Received.NextAsync(TestBus.Timeout));
                Assert.Equal(3, endpoint.Attempts);
                Assert.Equal(2, endpoint.Log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("retrying")));
                Assert.Null(await bus.GetAsync(endpoint.Bus.DeadLetterQueueName));
            }
        }

        [Fact]
        public async Task FailingHandler_IsDeadLettered_AfterMaxAttempts()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), Shared(maxAttempts: 3));
                endpoint.HandlerException = new InvalidOperationException("permanent");
                ICompatEndpoint sender = bus.AddEndpoint(CompatBuild.Current, "compat-current");
                await bus.WaitUntilConnectedAsync();

                sender.PublishRemote(CompatPayloads.Golden());

                CapturedMessage dead = await WaitForDeadLetterAsync(bus, endpoint);
                Assert.Equal(3, endpoint.Attempts);
                LogEntry logged = await endpoint.Log.WaitForAsync(e => e.Level == LogLevel.Error, TestBus.Timeout);
                Assert.NotNull(logged);
                Assert.Same(endpoint.HandlerException, logged.Exception);
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
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), Shared(maxAttempts: 3));
                await bus.WaitUntilConnectedAsync();

                byte[] poison = Encoding.UTF8.GetBytes("not json");
                await bus.PublishRawAsync(new Dictionary<string, object> { [WireHeaders.MessageType] = CoreEndpoint.WireName }, poison);

                Assert.Equal(poison, (await WaitForDeadLetterAsync(bus, endpoint)).Body);
                Assert.Equal(0, endpoint.Attempts);
            }
        }

        [Fact]
        public async Task UnknownMessage_IsDropped_NotDeadLettered()
        {
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                CoreEndpoint endpoint = bus.AddCoreEndpoint(NewServiceName(), Shared());
                await bus.WaitUntilConnectedAsync();

                // The queue is bound to the whole exchange, so events nothing here handles are normal: not poison.
                Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = "Something.Else" };
                await bus.PublishRawAsync(headers, GoldenFile.Read("compat-payload.json"));

                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Null(await bus.GetAsync(endpoint.Bus.QueueName));
                Assert.Null(await bus.GetAsync(endpoint.Bus.DeadLetterQueueName));
                Assert.Equal(0, endpoint.Attempts);
            }
        }

        [Fact]
        public async Task MessageThatKeepsCrashingTheConsumer_IsDeadLettered_ByTheDeliveryLimit()
        {
            // RabbitMQ 4.x counts deliveries lost with a connection (a crashed process), not requeues. The delivery
            // limit is the guard for a message that takes the whole process down every time it is handled.
            await using (TestBus bus = await TestBus.CreateAsync())
            {
                CoreEndpoint declaring = bus.AddCoreEndpoint(NewServiceName(), options =>
                {
                    Shared()(options);
                    options.DeliveryLimit = 2;
                });
                await bus.WaitUntilConnectedAsync(declaring);
                declaring.Dispose();

                await bus.PublishRawAsync(null, GoldenFile.Read("compat-payload.json"));

                // Keep crashing until the broker stops redelivering; dead-lettering happens asynchronously.
                int crashes = 0;
                while (crashes < 5 && await CrashWhileHandlingAsync(bus, declaring.Bus.QueueName))
                {
                    crashes++;
                }

                CapturedMessage dead = await WaitForDeadLetterAsync(bus, declaring);
                Assert.Equal("delivery_limit", dead.Headers["x-first-death-reason"]);
                Assert.Equal(GoldenFile.Read("compat-payload.json"), dead.Body);
                Assert.InRange(crashes, 1, 3);
            }
        }

        /// <summary>Takes one delivery and drops the connection without settling it, as a crashed process would.</summary>
        /// <returns>False when nothing was delivered within a few seconds.</returns>
        private static async Task<bool> CrashWhileHandlingAsync(TestBus bus, string queueName)
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

        private static async Task<CapturedMessage> WaitForDeadLetterAsync(TestBus bus, CoreEndpoint endpoint)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                CapturedMessage dead = await bus.GetAsync(endpoint.Bus.DeadLetterQueueName);
                if (dead != null)
                {
                    return dead;
                }

                Assert.True(stopwatch.Elapsed < TestBus.Timeout, "Nothing arrived in the dead-letter queue.");
                await Task.Delay(50);
            }
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(stopwatch.Elapsed < TestBus.Timeout, "Timed out.");
                await Task.Delay(50);
            }
        }

        private static async Task<List<CompatPayload>> DrainAsync(ReceivedPayloads received)
        {
            List<CompatPayload> payloads = new List<CompatPayload>();
            for (CompatPayload payload = await received.NextAsync(TimeSpan.Zero); payload != null; payload = await received.NextAsync(TimeSpan.Zero))
            {
                payloads.Add(payload);
            }

            return payloads;
        }
    }
}
