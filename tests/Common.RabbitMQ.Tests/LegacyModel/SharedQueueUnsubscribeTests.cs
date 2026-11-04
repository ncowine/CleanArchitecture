using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppA.Events;
using Common.RabbitMQ.Tests.Broker;
using Common.RabbitMQ.Tests.Golden;
using Messaging;
using Messaging.RabbitMQ;
using Xunit;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// A durable queue keeps its bindings and messages across restarts. Most tests start an instance that binds a message
    /// type, stop it, leave one message on the queue, then start the next instance.
    /// </summary>
    public class SharedQueueUnsubscribeTests
    {
        private const string App = "shared-unsubscribe";
        private const string WireName = "AppA.Events.Backlogged";

        /// <summary>How long to wait for something that must not happen.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task SubscribingBeforeStart_WithoutWaiting_ReceivesWhatIsAlreadyOnTheQueue()
        {
            await WithBacklog(async (topology, next, deadLetterQueue) =>
            {
                Received<Order> received = next.Record<Order>(WireName);
                RabbitMQBus bus = next.CreateBus();

                _ = bus.Subscribe(new MessageRegistration(WireName, typeof(Order)));
                next.Start();

                Assert.Equal(42, (await received.Next(TestBus.Timeout))?.Id);
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Null(await topology.Get(deadLetterQueue));
            });
        }

        [Fact]
        public async Task AMessageNobodyEverSubscribedTo_IsStillDeadLettered()
        {
            await WithBacklog(async (topology, next, deadLetterQueue) =>
            {
                next.Start();
                await topology.WaitUntilConnected(next);

                CapturedMessage deadLettered = null;
                for (int i = 0; i < 100 && deadLettered == null; i++)
                {
                    deadLettered = await topology.Get(deadLetterQueue);
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                Assert.NotNull(deadLettered);
            });
        }

        [Fact]
        public async Task ReleasedBeforeStart_CreatesNoBinding()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");
                CoreApp app = topology.CreateCore(App, options => Share(options, service));
                RabbitMQBus bus = app.CreateBus();

                await bus.Subscribe(new MessageRegistration(WireName, typeof(Order)));
                await bus.ReleaseSubscription(WireName);
                app.Start();
                await topology.WaitUntilConnected(app);

                Assert.False(await topology.IsRoutable(App, WireName));
            }
        }

        [Fact]
        public async Task ReleasedAfterStart_KeepsTheBinding_AndWarnsOnce_WhenAMessageArrives()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");
                RecordingLogger logger = new RecordingLogger();
                CoreApp app = topology.CreateCore(App, options => Share(options, service));
                Received<Order> received = app.Listen<Order>(WireName);
                RabbitMQBus bus = app.CreateBus(logger);
                app.Start();
                await topology.WaitUntilConnected(app);

                await bus.ReleaseSubscription(WireName);
                Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("no subscribers left"));

                Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
                await topology.PublishRaw(App, WireName, headers, GoldenFile.Read("compat-payload.json"));
                await topology.PublishRaw(App, WireName, headers, GoldenFile.Read("compat-payload.json"));
                for (int i = 0; i < 100 && received.Count < 2; i++)
                {
                    await Task.Delay(50, TestContext.Current.CancellationToken);
                }

                Assert.Equal(2, received.Count);
                Assert.Single(logger.Entries, e => e.Message.Contains("no subscribers left"));
            }
        }

        [Fact]
        public async Task SubscribingAfterStart_Warns_ButNotBeforeStart()
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                RecordingLogger logger = new RecordingLogger();
                CoreApp app = topology.CreateCore(App, options => Share(options, service));
                topology.DeleteOnDispose(service, service + ".dead-letter");
                RabbitMQBus bus = app.CreateBus(logger);

                await bus.Subscribe(new MessageRegistration("Before.Start", typeof(Order)));
                app.Start();
                await bus.Subscribe(new MessageRegistration("After.Start", typeof(Order)));

                LogEntry[] warnings = logger.Entries.Where(e => e.Message.Contains("after the bus started")).ToArray();
                Assert.Single(warnings);
                Assert.Contains("After.Start", warnings[0].Message);
            }
        }

        private static void Share(RabbitMQBusOptions options, string service)
        {
            options.QueueMode = QueueMode.Shared;
            options.SharedQueueName = service;
        }

        private static async Task WithBacklog(Func<LegacyTopology, CoreApp, string, Task> test)
        {
            await using (LegacyTopology topology = await LegacyTopology.Create())
            {
                string service = "svc-" + Guid.NewGuid().ToString("N");
                topology.DeleteOnDispose(service, service + ".dead-letter");

                CoreApp previous = topology.CreateCore(App, options => Share(options, service));
                previous.Listen<Order>(WireName);
                previous.Start();
                await topology.WaitUntilConnected(previous);
                previous.Dispose();

                Dictionary<string, object> headers = new Dictionary<string, object> { [WireHeaders.MessageType] = WireName };
                await topology.PublishRaw(App, WireName, headers, GoldenFile.Read("compat-payload.json"));

                await test(topology, topology.CreateCore(App, options => Share(options, service)), service + ".dead-letter");
            }
        }
    }
}
