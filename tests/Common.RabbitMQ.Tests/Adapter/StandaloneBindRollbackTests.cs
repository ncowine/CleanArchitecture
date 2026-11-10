using System;
using System.Collections;
using System.Reflection;
using Messaging;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Logging.Abstractions;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// Two RabbitMQService instances without a router: a keyed subscription that one of them refuses binds on neither.
    /// </summary>
    public class StandaloneBindRollbackTests
    {
        private const int UnreachablePort = 1;

        [Fact]
        public void ARefusedBind_IsUndoneOnTheServicesThatAlreadyBound()
        {
            EventAggregator events = new EventAggregator();
            using (RabbitMQService first = new RabbitMQService(events, new RemoteEventRegistry(), NullLogger.Instance))
            using (RabbitMQService second = new RabbitMQService(events, new RemoteEventRegistry(), NullLogger.Instance))
            {
                first.Init(Config("first", "shared"));
                second.Init(Config("second"));
                KeyedSecond keyed = events.GetEvent<KeyedSecond>();
                Action<global::AppA.Events.Order> action = _ => { };

                Assert.Throws<InvalidOperationException>(() =>
                    keyed.Subscribe(action, new SubscribeOptions { Exchange = "shared", RoutingKey = "k" }, keepSubscriberReferenceAlive: true));

                Assert.False(keyed.Contains(action));
                Assert.Equal(0, RequestedBindings(first));
                Assert.Equal(0, RequestedBindings(second));
            }
        }

        private static int RequestedBindings(RabbitMQService service)
        {
            RabbitMQBus bus = (RabbitMQBus)typeof(RabbitMQService).GetField("bus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
            return ((ICollection)typeof(RabbitMQBus).GetField("requestedBindings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bus)).Count;
        }

        private static RabbitMQConfig Config(string name, params string[] subscriptions)
        {
            RabbitMQConfig config = new RabbitMQConfig
            {
                BusName = "Main",
                HostName = "127.0.0.1",
                Port = UnreachablePort,
                ExchangeName = name,
                ClientName = "rollback-tests-" + name,
            };
            foreach (string exchange in subscriptions)
            {
                config.Subscriptions.Add(new RabbitMQSubscription { Exchange = exchange });
            }

            return config;
        }
    }
}
