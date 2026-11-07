using System;
using System.Collections.Generic;
using AppA.Events;
using Common.Events;
using Messaging;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary><c>PublishRemote</c> sends and raises locally; <c>PublishRemoteOnly</c> only sends. No broker needed.</summary>
    [Collection(ServiceProviderCollection.Name)]
    public class RemoteOnlyTests : IDisposable
    {
        public void Dispose()
        {
            RabbitMQServiceProvider.Current = null;
        }

        private static T Use<T>(T service) where T : IRabbitMQService
        {
            RabbitMQServiceProvider.Current = service;
            return service;
        }

        [Fact]
        public void PublishRemote_SendsAndRaisesLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = Use(new RecordingService());
            int local = 0;
            eventAggregator.GetEvent<OrderPlaced>().Subscribe(_ => local++);

            eventAggregator.GetEvent<OrderPlaced>().PublishRemote(new Order());

            Assert.Single(service.Sent);
            Assert.Equal(1, local);
        }

        [Fact]
        public void PublishRemoteOnly_SendsWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = Use(new RecordingService());
            int local = 0;
            eventAggregator.GetEvent<OrderPlaced>().Subscribe(_ => local++);

            eventAggregator.GetEvent<OrderPlaced>().PublishRemoteOnly(new Order());

            Assert.Equal(new KeyValuePair<Type, string>(typeof(OrderPlaced), null), Assert.Single(service.Sent));
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnly_WorksForRemotePubSubEvents()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = Use(new RecordingService());
            int local = 0;
            eventAggregator.GetEvent<EmployeeUpdated>().Subscribe(_ => local++);

            eventAggregator.GetEvent<EmployeeUpdated>().PublishRemoteOnly(new Employee());

            Assert.Equal(typeof(EmployeeUpdated), Assert.Single(service.Sent).Key);
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnly_WithARoutingKey_SendsItWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = Use(new RecordingService());
            int local = 0;
            eventAggregator.GetEvent<OrderPlaced>().Subscribe(_ => local++);

            eventAggregator.GetEvent<OrderPlaced>().PublishRemoteOnly(new Order(), new PublishOptions { RoutingKey = "orders.eu.saved" });

            Assert.Equal(new KeyValuePair<Type, string>(typeof(OrderPlaced), "orders.eu.saved"), Assert.Single(service.Sent));
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnly_WithARoutingKey_WithoutRoutingSupport_IsRefused()
        {
            EventAggregator eventAggregator = new EventAggregator();
            Use(new PublishOnlyService());

            Assert.Throws<NotSupportedException>(() =>
                eventAggregator.GetEvent<OrderPlaced>().PublishRemoteOnly(new Order(), new PublishOptions { RoutingKey = "orders.eu.saved" }));
        }
    }
}
