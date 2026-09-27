using Compat.Events;
using Prism.Events;

namespace Common.RabbitMQ.Tests.Broker
{
    public sealed class CurrentEndpoint : ICompatEndpoint
    {
        private readonly EventAggregator eventAggregator = new EventAggregator();
        private readonly RabbitMQService service;
        private readonly string queueName;

        public CurrentEndpoint(BrokerSettings broker, string exchangeName, string clientName)
        {
            service = new RabbitMQService(eventAggregator, new RemoteEventRegistry(typeof(CompatEvent).Assembly));
            eventAggregator.GetEvent<CompatEvent>().Subscribe(Received.Add, ThreadOption.PublisherThread, keepSubscriberReferenceAlive: true);
            service.Init(broker.CreateCurrentConfig(CompatBus.Name, exchangeName, clientName));
            queueName = $"{clientName}.{CompatBus.Name}.{service.InstanceId}".ToLowerInvariant();
        }

        public CompatBuild Build => CompatBuild.Current;

        public string InstanceId => service.InstanceId;

        public string QueueName => queueName;

        public bool IsConnected => service.IsConsumerConnected && service.IsPublisherConnected;

        public ReceivedPayloads Received { get; } = new ReceivedPayloads();

        public void PublishRemote(CompatPayload payload)
        {
            eventAggregator.GetEvent<CompatEvent>().PublishRemote(payload, service);
        }

        public void Dispose()
        {
            service.Dispose();
        }
    }
}
