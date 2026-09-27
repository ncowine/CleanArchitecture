extern alias baseline;

using Compat.Events;
using Prism.Events;
using BaselineCompatEvent = baseline::Compat.Events.CompatEvent;
using BaselineCompatPayload = baseline::Compat.Events.CompatPayload;
using BaselineConfig = baseline::Common.RabbitMQ.RabbitMQConfig;
using BaselineExtensions = baseline::Common.RabbitMQ.RemotePubSubEventExtensions;
using BaselineRegistry = baseline::Common.RabbitMQ.RemoteEventRegistry;
using BaselineService = baseline::Common.RabbitMQ.RabbitMQService;

namespace Common.RabbitMQ.Tests.Broker
{
    /// <summary>
    /// The frozen baseline build. Its types share names with the current build, so everything goes through aliases;
    /// inside this namespace an unqualified <c>RabbitMQService</c> would bind to the current build.
    /// </summary>
    public sealed class BaselineEndpoint : ICompatEndpoint
    {
        private readonly EventAggregator eventAggregator = new EventAggregator();
        private readonly BaselineService service;
        private readonly string queueName;

        public BaselineEndpoint(BrokerSettings broker, string exchangeName, string clientName)
        {
            service = new BaselineService(eventAggregator, new BaselineRegistry(typeof(BaselineCompatEvent).Assembly));
            eventAggregator.GetEvent<BaselineCompatEvent>().Subscribe(OnReceived, ThreadOption.PublisherThread, keepSubscriberReferenceAlive: true);

            BaselineConfig config = new BaselineConfig
            {
                BusName = CompatBus.Name,
                HostName = broker.HostName,
                Port = broker.Port,
                VirtualHost = broker.VirtualHost,
                UserName = broker.UserName,
                Password = broker.Password,
                ExchangeName = exchangeName,
                ClientName = clientName,
                ReconnectDelaySeconds = 1,
                OutstandingPollIntervalMilliseconds = 20,
            };
            service.Init(config);
            queueName = $"{clientName}.{CompatBus.Name}.{service.InstanceId}".ToLowerInvariant();
        }

        public CompatBuild Build => CompatBuild.Baseline;

        public string InstanceId => service.InstanceId;

        public string QueueName => queueName;

        public bool IsConnected => service.IsConsumerConnected && service.IsPublisherConnected;

        public ReceivedPayloads Received { get; } = new ReceivedPayloads();

        public void PublishRemote(CompatPayload payload)
        {
            BaselineCompatPayload baselinePayload = new BaselineCompatPayload
            {
                Id = payload.Id,
                Name = payload.Name,
                UpdatedAt = payload.UpdatedAt,
            };

            BaselineExtensions.PublishRemote(eventAggregator.GetEvent<BaselineCompatEvent>(), baselinePayload, service);
        }

        public void Dispose()
        {
            service.Dispose();
        }

        private void OnReceived(BaselineCompatPayload payload)
        {
            Received.Add(new CompatPayload
            {
                Id = payload.Id,
                Name = payload.Name,
                UpdatedAt = payload.UpdatedAt,
            });
        }
    }
}
