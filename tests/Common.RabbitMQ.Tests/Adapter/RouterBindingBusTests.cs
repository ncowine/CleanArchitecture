using System;
using System.Reflection;
using Messaging;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// Which of the router's buses a subscription's routing key is bound on. The rule is internal, so it is called through
    /// reflection; <c>HostingRoutingKeyBusTests</c> checks the same rule on the broker for the modern library.
    /// </summary>
    public class RouterBindingBusTests
    {
        private const int UnreachablePort = 1;

        [Fact]
        public void TheBusOption_Wins()
        {
            using (RabbitMQServiceRouter router = Router())
            {
                Assert.Equal("First", BusFor(router, new SubscribeOptions { Bus = "First", Exchange = "second-app", RoutingKey = "k" }));
            }
        }

        [Fact]
        public void WithoutABus_TheBusThatUsesTheExchange()
        {
            using (RabbitMQServiceRouter router = Router())
            {
                Assert.Equal("Second", BusFor(router, new SubscribeOptions { Exchange = "second-app", RoutingKey = "k" }));
                Assert.Equal("Second", BusFor(router, new SubscribeOptions { Exchange = "second", RoutingKey = "k" }));
            }
        }

        [Fact]
        public void AnExchangeNoBusUses_OrNone_GoesToTheDefaultBus()
        {
            using (RabbitMQServiceRouter router = Router())
            {
                Assert.Equal("First", BusFor(router, new SubscribeOptions { Exchange = "elsewhere", RoutingKey = "k" }));
                Assert.Equal("First", BusFor(router, new SubscribeOptions { RoutingKey = "k" }));
            }
        }

        [Fact]
        public void AnExchangeOnSeveralBuses_OrAnUnknownBus_Throws()
        {
            using (RabbitMQServiceRouter router = Router())
            {
                InvalidOperationException ambiguous = Assert.Throws<InvalidOperationException>(() => BusFor(router, new SubscribeOptions { Exchange = "OldApp", RoutingKey = "k" }));
                Assert.Contains("SubscribeOptions.Bus", ambiguous.Message);

                InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() => BusFor(router, new SubscribeOptions { Bus = "Mistyped", RoutingKey = "k" }));
                Assert.Contains("Mistyped", unknown.Message);
            }
        }

        [Fact]
        public void SubscribingWithAnAmbiguousKey_Throws_AndLeavesNoSubscription()
        {
            EventAggregator events = new EventAggregator();
            using (RabbitMQServiceRouter router = Router(events))
            {
                router.Init();
                KeyedSecond keyed = events.GetEvent<KeyedSecond>();
                Action<global::AppA.Events.Order> action = _ => { };

                Assert.Throws<InvalidOperationException>(() =>
                    keyed.Subscribe(action, new SubscribeOptions { Exchange = "OldApp", RoutingKey = "k" }, keepSubscriberReferenceAlive: true));
                Assert.False(keyed.Contains(action));
            }
        }

        /// <summary>First (default) owns "first"; Second owns "second" and subscribes to "second-app"; both subscribe to "OldApp".</summary>
        private static RabbitMQServiceRouter Router(EventAggregator events = null)
        {
            RabbitMQServiceRouter router = new RabbitMQServiceRouter(events ?? new EventAggregator(), new RemoteEventRegistry()) { DefaultBus = "First" };
            RabbitMQConfig first = Config("First");
            first.Subscriptions.Add(new RabbitMQSubscription { Exchange = "OldApp" });
            RabbitMQConfig second = Config("Second");
            second.Subscriptions.Add(new RabbitMQSubscription { Exchange = "second-app" });
            second.Subscriptions.Add(new RabbitMQSubscription { Exchange = "OldApp" });
            router.AddBus(first);
            router.AddBus(second);
            return router;
        }

        private static RabbitMQConfig Config(string busName)
        {
            return new RabbitMQConfig
            {
                BusName = busName,
                HostName = "127.0.0.1",
                Port = UnreachablePort,
                ExchangeName = busName.ToLowerInvariant(),
                ClientName = "binding-bus-tests",
            };
        }

        private static string BusFor(RabbitMQServiceRouter router, SubscribeOptions options)
        {
            MethodInfo method = typeof(RabbitMQServiceRouter).GetMethod("BusForBinding", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                return (string)method.Invoke(router, new object[] { options });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
