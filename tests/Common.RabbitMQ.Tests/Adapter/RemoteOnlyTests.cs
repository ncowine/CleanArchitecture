using System;
using System.Collections.Generic;
using AppA.Events;
using Common.Events;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Adapter
{
    /// <summary><c>PublishRemote</c> sends and raises locally; <c>PublishRemoteOnly</c> only sends. No broker needed.</summary>
    public class RemoteOnlyTests
    {
        [Fact]
        public void PublishRemote_SendsAndRaisesLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = new RecordingService();
            int local = 0;
            eventAggregator.GetEvent<OrderSaved>().Subscribe(_ => local++);

            eventAggregator.GetEvent<OrderSaved>().PublishRemote(new Order(), service);

            Assert.Single(service.Sent);
            Assert.Equal(1, local);
        }

        [Fact]
        public void PublishRemoteOnly_SendsWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = new RecordingService();
            int local = 0;
            eventAggregator.GetEvent<OrderSaved>().Subscribe(_ => local++);

            eventAggregator.GetEvent<OrderSaved>().PublishRemoteOnly(new Order(), service);

            Assert.Equal(new KeyValuePair<Type, string>(typeof(OrderSaved), null), Assert.Single(service.Sent));
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnly_WorksForRemotePubSubEvents()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = new RecordingService();
            int local = 0;
            eventAggregator.GetEvent<EmployeeUpdated>().Subscribe(_ => local++);

            eventAggregator.GetEvent<EmployeeUpdated>().PublishRemoteOnly(new Employee(), service);

            Assert.Equal(typeof(EmployeeUpdated), Assert.Single(service.Sent).Key);
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnlyTo_SendsWithTheRoutingKeyWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingService service = new RecordingService();
            int local = 0;
            eventAggregator.GetEvent<OrderSaved>().Subscribe(_ => local++);

            eventAggregator.GetEvent<OrderSaved>().PublishRemoteOnlyTo("orders.eu.saved", new Order(), service);

            Assert.Equal(new KeyValuePair<Type, string>(typeof(OrderSaved), "orders.eu.saved"), Assert.Single(service.Sent));
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnlyTo_WithoutRoutingSupport_IsRefused()
        {
            EventAggregator eventAggregator = new EventAggregator();

            Assert.Throws<NotSupportedException>(() =>
                eventAggregator.GetEvent<OrderSaved>().PublishRemoteOnlyTo("orders.eu.saved", new Order(), new PublishOnlyService()));
        }
    }
}
