using System;
using System.Linq;
using AppA.Events;
using Common.Events;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// <see cref="RemoteEventRegistry.Add"/> and unassigned events (ADR 0002, section 4). None of these need a broker:
    /// services point at a port nothing listens on and only buffer messages.
    /// </summary>
    public class RegistryAdditionsTests
    {
        /// <summary>Nothing listens here: services start their loops but never connect.</summary>
        private const int UnreachablePort = 1;

        [Fact]
        public void Add_FindsPlainPubSubEvents_Unassigned()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);

            Assert.True(registry.TryGet(typeof(OrderSaved), out RemoteEventDescriptor descriptor));
            Assert.Equal("AppA.Events.OrderSaved", descriptor.EventName);
            Assert.Equal(typeof(Order), descriptor.PayloadType);
            Assert.Null(descriptor.BusName);
            Assert.True(registry.TryGet(typeof(OrderSelected), out RemoteEventDescriptor local));
        }

        [Fact]
        public void Add_WithABus_AssignsIt()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly, "Orders");

            Assert.Contains(registry.GetByBus("Orders"), d => d.EventType == typeof(OrderSaved));
            Assert.DoesNotContain(registry.GetByBus("Other"), d => d.EventType == typeof(OrderSaved));
        }

        [Fact]
        public void UnassignedEvents_BelongToEveryBus()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);

            Assert.Contains(registry.GetByBus("Any"), d => d.EventType == typeof(OrderSaved));
            Assert.Contains(registry.GetByBus("Other"), d => d.EventType == typeof(OrderSaved));
        }

        [Fact]
        public void Add_AttributeBusWins_OverTheAssemblysBus()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(EmployeeUpdated).Assembly, "Other");

            Assert.True(registry.TryGet(typeof(EmployeeUpdated), out RemoteEventDescriptor attributed));
            Assert.Equal(BusNames.Legacy, attributed.BusName);
            Assert.True(registry.TryGet(typeof(EmployeeSelected), out RemoteEventDescriptor plain));
            Assert.Equal("Other", plain.BusName);
        }

        [Fact]
        public void Constructor_IsUnchanged_PlainEventsStayLocal()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry(typeof(EmployeeUpdated).Assembly);

            Assert.True(registry.TryGet(typeof(EmployeeUpdated), out RemoteEventDescriptor descriptor));
            Assert.False(registry.TryGet(typeof(EmployeeSelected), out descriptor));
            Assert.Equal(3, registry.Events.Count);
        }

        [Fact]
        public void Router_UnassignedEvent_GoesToTheOnlyBus()
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
        public void Router_UnassignedEvent_WithSeveralBuses_IsRefused()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly);
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry))
            {
                router.AddBus(UnreachableConfig("First"));
                router.AddBus(UnreachableConfig("Second"));
                router.Init();

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => router.Publish(typeof(OrderSaved), new Order { Id = 1 }));
                Assert.Contains("has no bus", error.Message);
                Assert.True(router.Buses.All(b => b.OutstandingCount == 0));
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
