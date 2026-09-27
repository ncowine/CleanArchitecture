using System;
using System.Threading;
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
    /// <see cref="IMessageSubscriber"/>: receiving in ordinary code (for example a WPF view model) with no handler classes and
    /// no Prism, against the frozen baseline Prism build over a real broker.
    /// </summary>
    public class HostingSubscriberTests
    {
        /// <summary>How long to keep listening for a message that must not arrive.</summary>
        private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task Subscribe_ReceivesMessages_UntilDisposed()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => messaging.AddMessages(typeof(CompatMessage).Assembly)))
                {
                    Received<CompatMessage> received = new Received<CompatMessage>();
                    IDisposable subscription = host.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(received.Add);
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());
                    CompatMessage message = await received.Next(TestBus.Timeout);
                    Assert.Equal(42, message?.Id);
                    Assert.Equal("compat", message.Name);

                    subscription.Dispose();
                    subscription.Dispose();
                    baseline.PublishRemote(CompatPayloads.Golden());
                    await Task.Delay(QuietPeriod, TestContext.Current.CancellationToken);
                    Assert.Equal(1, received.Count);
                }
            }
        }

        [Fact]
        public async Task Subscribe_WithASynchronizationContext_PostsToIt()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => messaging.AddMessages(typeof(CompatMessage).Assembly)))
                {
                    RecordingSynchronizationContext uiContext = new RecordingSynchronizationContext();
                    Received<CompatMessage> received = new Received<CompatMessage>();
                    host.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>(received.Add, uiContext);
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());

                    Assert.NotNull(await received.Next(TestBus.Timeout));
                    Assert.Equal(1, uiContext.Posts);
                }
            }
        }

        [Fact]
        public async Task SeveralSubscribers_AllReceive_InOrder()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");

                await using (TestHost host = await TestHost.Start(bus, "compat-app", messaging => messaging.AddMessages(typeof(CompatMessage).Assembly)))
                {
                    IMessageSubscriber subscriber = host.Services.GetRequiredService<IMessageSubscriber>();
                    string order = "";
                    TaskCompletionSource<bool> done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    subscriber.Subscribe<CompatMessage>(m => order += "first,");
                    subscriber.Subscribe<CompatMessage>((m, context, token) =>
                    {
                        order += $"second:{context.WireName}";
                        done.TrySetResult(true);
                        return Task.CompletedTask;
                    });
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());

                    Assert.True(await Task.WhenAny(done.Task, Task.Delay(TestBus.Timeout, TestContext.Current.CancellationToken)) == done.Task);
                    Assert.Equal("first,second:Compat.Events.CompatEvent", order);
                }
            }
        }

        [Fact]
        public async Task AsyncSubscriberFailure_FailsTheMessage_RetriedOnASharedQueue()
        {
            await using (TestBus bus = await TestBus.Create())
            {
                ICompatEndpoint baseline = bus.AddEndpoint(CompatBuild.Baseline, "compat-baseline");
                string service = "svc-" + Guid.NewGuid().ToString("N");

                await using (TestHost host = await TestHost.Start(
                    bus,
                    service,
                    messaging => messaging.AddMessages(typeof(CompatMessage).Assembly),
                    options =>
                    {
                        options.QueueMode = QueueMode.Shared;
                        options.MaxAttempts = 3;
                        options.RetryDelay = TimeSpan.FromMilliseconds(50);
                    }))
                {
                    bus.DeleteOnDispose(host.Bus.QueueName, host.Bus.DeadLetterQueueName);
                    int attempts = 0;
                    host.Services.GetRequiredService<IMessageSubscriber>().Subscribe<CompatMessage>((m, context, token) =>
                    {
                        Interlocked.Increment(ref attempts);
                        throw new InvalidOperationException("Subscriber failed on purpose.");
                    });
                    await bus.WaitUntilConnected();
                    await host.WaitUntilConnected();

                    baseline.PublishRemote(CompatPayloads.Golden());

                    CapturedMessage dead = null;
                    for (int wait = 0; wait < 100 && dead == null; wait++)
                    {
                        await Task.Delay(100, TestContext.Current.CancellationToken);
                        dead = await bus.Get(host.Bus.DeadLetterQueueName);
                    }

                    Assert.NotNull(dead);
                    Assert.Equal(3, Volatile.Read(ref attempts));
                    Assert.Equal(GoldenFile.Read("compat-payload.json"), dead.Body);
                }
            }
        }
    }
}
