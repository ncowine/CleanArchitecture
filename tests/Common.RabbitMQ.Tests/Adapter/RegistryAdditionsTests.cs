using System;
using System.Linq;
using AppA.Events;
using Common.Events;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// <see cref="RemoteEventRegistry.Add"/> and choosing the bus when publishing (ADR 0002). None of these need a broker:
    /// services point at a port nothing listens on and only buffer messages.
    /// </summary>
    public class RegistryAdditionsTests
    {
        /// <summary>Nothing listens here: services start their loops but never connect.</summary>
        private const int UnreachablePort = 1;

        [Fact]
        public void Add_FindsPlainPubSubEvents()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);

            Assert.True(registry.TryGet(typeof(OrderSaved), out RemoteEventDescriptor descriptor));
            Assert.Equal("AppA.Events.OrderSaved", descriptor.EventName);
            Assert.Equal(typeof(Order), descriptor.PayloadType);
            Assert.True(registry.TryGet(typeof(OrderSelected), out RemoteEventDescriptor local));
        }

        [Fact]
        public void Constructor_FindsRemoteEvents_PlainEventsStayLocal()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry(typeof(EmployeeUpdated).Assembly);

            Assert.True(registry.TryGet(typeof(EmployeeUpdated), out RemoteEventDescriptor descriptor));
            Assert.False(registry.TryGet(typeof(EmployeeSelected), out descriptor));
            Assert.Equal(3, registry.Events.Count);
        }

        [Fact]
        public void Router_WithOneBus_NeedsNoDefault()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry))
            {
                RabbitMQService only = router.AddBus(UnreachableConfig("Only"));
                router.Init();

                router.Publish(typeof(OrderSaved), new Order { Id = 1 });

                Assert.Equal(1, only.OutstandingCount);
            }
        }

        [Fact]
        public void Router_WithSeveralBuses_AndNoDefault_IsRefused()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry))
            {
                router.AddBus(UnreachableConfig("First"));
                router.AddBus(UnreachableConfig("Second"));
                router.Init();

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => router.Publish(typeof(OrderSaved), new Order { Id = 1 }));
                Assert.Contains("no default", error.Message);
                Assert.True(router.Buses.All(b => b.OutstandingCount == 0));
            }
        }

        [Fact]
        public void Router_SendsToTheDefaultBus()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry) { DefaultBus = "Second" })
            {
                RabbitMQService first = router.AddBus(UnreachableConfig("First"));
                RabbitMQService second = router.AddBus(UnreachableConfig("Second"));
                router.Init();

                new EventAggregator().GetEvent<OrderSaved>().PublishRemote(new Order { Id = 1 }, router);

                Assert.Equal(0, first.OutstandingCount);
                Assert.Equal(1, second.OutstandingCount);
            }
        }

        [Fact]
        public void PublishRemote_ToANamedBus_OverridesTheDefault()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry) { DefaultBus = "Second" })
            {
                RabbitMQService first = router.AddBus(UnreachableConfig("First"));
                RabbitMQService second = router.AddBus(UnreachableConfig("Second"));
                router.Init();

                new EventAggregator().GetEvent<OrderSaved>().PublishRemote(new Order { Id = 1 }, router.GetRequiredBus("First"));
                new EventAggregator().GetEvent<OrderSaved>().PublishRemoteTo("orders.eu.saved", new Order { Id = 2 }, router.GetRequiredBus("First"));

                Assert.Equal(2, first.OutstandingCount);
                Assert.Equal(0, second.OutstandingCount);
            }
        }

        [Fact]
        public void Router_GetRequiredBus_UnknownName_Throws()
        {
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), new RemoteEventRegistry()))
            {
                router.AddBus(UnreachableConfig("Only"));

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => router.GetRequiredBus("Mistyped"));
                Assert.Contains("Mistyped", error.Message);
            }
        }

        [Fact]
        public void Router_UnknownDefaultBus_FailsAtInit()
        {
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), new RemoteEventRegistry()) { DefaultBus = "Mistyped" })
            {
                router.AddBus(UnreachableConfig("Only"));

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => router.Init());
                Assert.Contains("Mistyped", error.Message);
            }
        }

        [Fact]
        public void PublishRemoteTo_WithoutRoutingSupport_IsRefused()
        {
            EventAggregator eventAggregator = new EventAggregator();

            Assert.Throws<NotSupportedException>(() =>
                eventAggregator.GetEvent<OrderSaved>().PublishRemoteTo("orders.eu.saved", new Order(), new PublishOnlyService()));
        }

        private static RabbitMQConfig UnreachableConfig(string busName)
        {
            return new RabbitMQConfig
            {
                BusName = busName,
                HostName = "127.0.0.1",
                Port = UnreachablePort,
                ExchangeName = busName.ToLowerInvariant(),
                ClientName = "registry-tests",
            };
        }
    }
}
