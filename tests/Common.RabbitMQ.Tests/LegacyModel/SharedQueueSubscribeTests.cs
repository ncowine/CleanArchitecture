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
    /// A durable queue keeps its messages across restarts. Most tests start an instance, stop it, leave one message on the
    /// queue, then start the next instance.
    /// </summary>
    public class SharedQueueSubscribeTests
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

                bus.Subscribe(new MessageRegistration(WireName, typeof(Order)));
                next.Start();

                Assert.Equal(42, (await received.Next(TestBus.Timeout))?.Id);
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Null(await topology.Get(deadLetterQueue));
            });
        }

        [Fact]
        public async Task AMessageNobodySubscribesTo_IsDropped_NotDeadLettered()
        {
            await WithBacklog(async (topology, next, deadLetterQueue) =>
            {
                next.Start();
                await topology.WaitUntilConnected(next);

                // The queue is bound to the whole exchange, so events this service doesn't handle are normal.
                await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                Assert.Null(await topology.Get(deadLetterQueue));
                Assert.Null(await topology.Get(deadLetterQueue.Substring(0, deadLetterQueue.Length - ".dead-letter".Length)));
            });
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

                bus.Subscribe(new MessageRegistration("Before.Start", typeof(Order)));
                app.Start();
                bus.Subscribe(new MessageRegistration("After.Start", typeof(Order)));

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
                await topology.PublishRaw(App, topology.Exchange(App), headers, GoldenFile.Read("compat-payload.json"));

                await test(topology, topology.CreateCore(App, options => Share(options, service)), service + ".dead-letter");
            }
        }
    }
}
