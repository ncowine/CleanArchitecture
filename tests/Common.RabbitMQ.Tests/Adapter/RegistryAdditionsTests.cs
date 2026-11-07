using System;
using AppA.Events;
using Common.Events;
using Messaging;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary>
    /// Which events travel, their wire names, and choosing the bus when publishing (ADR 0002). None of these need a broker:
    /// services point at a port nothing listens on and only buffer messages.
    /// </summary>
    [Collection(ServiceProviderCollection.Name)]
    public class RegistryAdditionsTests : IDisposable
    {
        /// <summary>Nothing listens here: services start their loops but never connect.</summary>
        private const int UnreachablePort = 1;

        public void Dispose()
        {
            RabbitMQServiceProvider.Current = null;
        }

        [Fact]
        public void WireName_IsTheFullTypeName_UnlessAMessageAttributeNamesIt()
        {
            Assert.Equal("Common.RabbitMQ.Tests.Adapter.OrderPlaced", new RemoteEventDescriptor(typeof(OrderPlaced), typeof(Order)).EventName);
            Assert.Equal("AppA.Events.OrderSaved", new RemoteEventDescriptor(typeof(LegacyModel.RemoteOrderSaved), typeof(Order)).EventName);
        }

        [Fact]
        public void APlainEvent_StaysLocal()
        {
            using (RabbitMQService service = new RabbitMQService(new EventAggregator(), new RemoteEventRegistry()))
            {
                service.Init(UnreachableConfig("Main"));

                Assert.Throws<ArgumentException>(() => service.Publish(typeof(OrderSaved), new Order()));
                Assert.Equal(0, service.OutstandingCount);
            }
        }

        [Fact]
        public void TwoEventsWithOneWireName_AreRefused()
        {
            using (RabbitMQService service = new RabbitMQService(new EventAggregator(), new RemoteEventRegistry()))
            {
                service.Init(UnreachableConfig("Main"));
                service.Publish(typeof(OrderPlaced), new Order());

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => service.Publish(typeof(SameWireName), new Order()));
                Assert.Contains("[Message]", error.Message);
            }
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
        public void Router_WithoutADefault_IsRefusedAtInit_EvenWithOneBus()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry();
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry))
            {
                RabbitMQService only = router.AddBus(UnreachableConfig("Only"));

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => router.Init());
                Assert.Contains("Messaging:DefaultBus", error.Message);
                Assert.Throws<InvalidOperationException>(() => router.Publish(typeof(OrderPlaced), new Order { Id = 1 }));
                Assert.Equal(0, only.OutstandingCount);
            }
        }

        [Fact]
        public void Router_SendsToTheDefaultBus()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry();
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry) { DefaultBus = "Second" })
            {
                RabbitMQService first = router.AddBus(UnreachableConfig("First"));
                RabbitMQService second = router.AddBus(UnreachableConfig("Second"));
                router.Init();
                RabbitMQServiceProvider.Current = router;

                new EventAggregator().GetEvent<OrderPlaced>().PublishRemote(new Order { Id = 1 });

                Assert.Equal(0, first.OutstandingCount);
                Assert.Equal(1, second.OutstandingCount);
            }
        }

        [Fact]
        public void PublishRemote_ToANamedBus_OverridesTheDefault()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry();
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry) { DefaultBus = "Second" })
            {
                RabbitMQService first = router.AddBus(UnreachableConfig("First"));
                RabbitMQService second = router.AddBus(UnreachableConfig("Second"));
                router.Init();
                RabbitMQServiceProvider.Current = router;

                new EventAggregator().GetEvent<OrderPlaced>().PublishRemote(new Order { Id = 1 }, new PublishOptions { Bus = "First" });
                new EventAggregator().GetEvent<OrderPlaced>().PublishRemote(new Order { Id = 2 }, new PublishOptions { Bus = "First", RoutingKey = "orders.eu.saved" });
                new EventAggregator().GetEvent<OrderPlaced>().PublishRemoteOnly(new Order { Id = 3 }, new PublishOptions { Bus = "First" });

                Assert.Equal(3, first.OutstandingCount);
                Assert.Equal(0, second.OutstandingCount);
            }
        }

        [Fact]
        public void PublishRemote_ToAnUnknownBus_ThrowsBeforeRaisingLocally()
        {
            RemoteEventRegistry registry = new RemoteEventRegistry();
            using (RabbitMQServiceRouter router = new RabbitMQServiceRouter(new EventAggregator(), registry) { DefaultBus = "Only" })
            {
                RabbitMQService only = router.AddBus(UnreachableConfig("Only"));
                router.Init();
                RabbitMQServiceProvider.Current = router;
                EventAggregator eventAggregator = new EventAggregator();
                int local = 0;
                eventAggregator.GetEvent<OrderPlaced>().Subscribe(_ => local++);

                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                    eventAggregator.GetEvent<OrderPlaced>().PublishRemote(new Order { Id = 1 }, new PublishOptions { Bus = "Mistyped" }));

                Assert.Contains("Mistyped", error.Message);
                Assert.Equal(0, only.OutstandingCount);
                Assert.Equal(0, local);
            }
        }

        [Fact]
        public void PublishRemote_ToANamedBus_WithoutARouter_IsRefused()
        {
            RabbitMQServiceProvider.Current = new PublishOnlyService();

            Assert.Throws<NotSupportedException>(() =>
                new EventAggregator().GetEvent<OrderPlaced>().PublishRemote(new Order(), new PublishOptions { Bus = "Partners" }));
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
        public void PublishRemote_WithARoutingKey_WithoutRoutingSupport_IsRefused()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RabbitMQServiceProvider.Current = new PublishOnlyService();

            Assert.Throws<NotSupportedException>(() =>
                eventAggregator.GetEvent<OrderPlaced>().PublishRemote(new Order(), new PublishOptions { RoutingKey = "orders.eu.saved" }));
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
