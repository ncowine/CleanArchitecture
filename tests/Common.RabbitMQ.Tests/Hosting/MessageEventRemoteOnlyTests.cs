using Messaging;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    // Inside the namespace, or the adapter's PublishRemote from the enclosing Common.RabbitMQ namespace wins.
    using Messaging.Prism;

    /// <summary><see cref="MessageEventExtensions"/> without a broker: what is sent, and whether local subscribers run.</summary>
    [Collection(PrismContainerCollection.Name)]
    public class MessageEventRemoteOnlyTests
    {
        [Fact]
        public void PublishRemote_SendsAndRaisesLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            using (new PublisherInContainer(publisher))
            {
                eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemote(new CompatMessage());
            }

            Assert.Null(Assert.Single(publisher.RoutingKeys));
            Assert.Null(Assert.Single(publisher.Buses));
            Assert.Equal(1, local);
        }

        [Fact]
        public void EveryMethod_PassesTheOptionsToThePublisher()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            using (new PublisherInContainer(publisher))
            {
                PublishOptions options = new PublishOptions { Bus = "Partners", RoutingKey = "compat.eu" };
                eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemote(new CompatMessage(), options);
                eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemoteOnly(new CompatMessage(), options);
            }

            Assert.Equal(new[] { "Partners", "Partners" }, publisher.Buses);
            Assert.Equal(new[] { "compat.eu", "compat.eu" }, publisher.RoutingKeys);
            Assert.Equal(1, local);
        }

        [Fact]
        public void PublishRemoteOnly_SendsWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            using (new PublisherInContainer(publisher))
            {
                eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemoteOnly(new CompatMessage());
            }

            Assert.Null(Assert.Single(publisher.RoutingKeys));
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnly_WithARoutingKey_SendsItWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            using (new PublisherInContainer(publisher))
            {
                eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemoteOnly(new CompatMessage(), new PublishOptions { RoutingKey = "compat.eu" });
            }

            Assert.Equal("compat.eu", Assert.Single(publisher.RoutingKeys));
            Assert.Equal(0, local);
        }
    }
}
