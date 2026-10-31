using Messaging.Prism;
using Prism.Events;
using Xunit;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary><see cref="MessageEventExtensions"/> without a broker: what is sent, and whether local subscribers run.</summary>
    public class MessageEventRemoteOnlyTests
    {
        [Fact]
        public void PublishRemote_SendsAndRaisesLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemote(new CompatMessage(), publisher);

            Assert.Null(Assert.Single(publisher.RoutingKeys));
            Assert.Equal(1, local);
        }

        [Fact]
        public void PublishRemoteOnly_SendsWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemoteOnly(new CompatMessage(), publisher);

            Assert.Null(Assert.Single(publisher.RoutingKeys));
            Assert.Equal(0, local);
        }

        [Fact]
        public void PublishRemoteOnlyTo_SendsWithTheRoutingKeyWithoutRaisingLocally()
        {
            EventAggregator eventAggregator = new EventAggregator();
            RecordingPublisher publisher = new RecordingPublisher();
            int local = 0;
            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().Subscribe(_ => local++);

            eventAggregator.GetEvent<MessageEvent<CompatMessage>>().PublishRemoteOnlyTo("compat.eu", new CompatMessage(), publisher);

            Assert.Equal("compat.eu", Assert.Single(publisher.RoutingKeys));
            Assert.Equal(0, local);
        }
    }
}
