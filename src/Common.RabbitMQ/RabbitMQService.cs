using System;
using System.Linq;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Logging;
using Prism.Events;

namespace Common.RabbitMQ
{
    public class RabbitMQService : IRabbitMQService, IRoutingKeyPublisher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;
        private readonly ILogger logger;
        private readonly string instanceId = Guid.NewGuid().ToString("N");

        private RabbitMQConfig config;
        private RabbitMQBus bus;
        private bool disposed;

        internal RabbitMQService(IEventAggregator eventAggregator, RemoteEventRegistry registry, ILogger logger)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        internal string BusName => config?.BusName;

        internal string InstanceId => instanceId;

        public bool IsConsumerConnected => bus != null && bus.IsConsumerConnected;

        internal bool IsPublisherConnected => bus != null && bus.IsPublisherConnected;

        internal int OutstandingCount => bus?.OutstandingCount ?? 0;

        internal void Init(RabbitMQConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (this.config != null)
            {
                throw new InvalidOperationException($"RabbitMQService for bus '{this.config.BusName}' is already initialized.");
            }

            if (string.IsNullOrWhiteSpace(config.BusName) || string.IsNullOrWhiteSpace(config.ExchangeName))
            {
                throw new ArgumentException("BusName and ExchangeName are required.", nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.ClientName))
            {
                config.ClientName = AppDomain.CurrentDomain.FriendlyName;
            }

            this.config = config;

            bus = new RabbitMQBus(CreateBusOptions(config), new PrismDispatcher(eventAggregator, registry), logger);
        }

        internal void Start(int subscribedEvents)
        {
            bus.Start();

            logger.LogInformation("[{Bus}] Initialized with {Count} event(s) on {Host}:{Port}{VirtualHost} exchange '{Exchange}'.",
                config.BusName, subscribedEvents, config.HostName, config.Port, FormatVirtualHost(config.VirtualHost), config.ExchangeName);
        }

        public void Publish(Type eventType, object payload)
        {
            EnsureInitialized();

            RemoteEventMetadata metadata = GetRequiredMetadata(eventType);
            bus.Publish(metadata.EventName, payload);
        }

        void IRoutingKeyPublisher.Publish(Type eventType, object payload, string routingKey)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(routingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(routingKey));
            }

            RemoteEventMetadata metadata = GetRequiredMetadata(eventType);
            bus.Publish(metadata.EventName, payload, routingKey);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            bus?.Dispose();
        }

        internal void StartReceiving(RemoteEventMetadata metadata)
        {
            bus.StartReceiving(new MessageRegistration(metadata.EventName, metadata.PayloadType));
        }

        internal void StopReceiving(RemoteEventMetadata metadata)
        {
            bus.StopReceiving(metadata.EventName);
        }

        internal void BindRoutingKey(string exchange, string routingKey)
        {
            bus.BindRoutingKey(exchange, routingKey);
        }

        internal void UnbindRoutingKey(string exchange, string routingKey)
        {
            bus.UnbindRoutingKey(exchange, routingKey);
        }

        private RabbitMQBusOptions CreateBusOptions(RabbitMQConfig config)
        {
            return new RabbitMQBusOptions
            {
                BusName = config.BusName,
                HostName = config.HostName,
                Port = config.Port,
                VirtualHost = config.VirtualHost,
                UserName = config.UserName,
                Password = config.Password,
                UseTls = config.UseTls,
                TlsServerName = config.TlsServerName,
                ExchangeName = config.ExchangeName,
                ClientName = config.ClientName,
                InstanceId = instanceId,
                ReconnectDelay = TimeSpan.FromSeconds(config.ReconnectDelaySeconds),
                OutstandingPollInterval = TimeSpan.FromMilliseconds(config.OutstandingPollIntervalMilliseconds),
                PrefetchCount = (ushort)config.PrefetchCount,
                Heartbeat = TimeSpan.FromSeconds(config.HeartbeatSeconds),
                ExchangeType = string.IsNullOrWhiteSpace(config.ExchangeType) ? "topic" : config.ExchangeType,
                RoutingKeyPrefix = config.RoutingKeyPrefix,
                Subscriptions = config.Subscriptions
                    .Where(s => s != null)
                    .Select(s => new SubscriptionOptions { Exchange = s.Exchange })
                    .ToList(),
            };
        }

        private RemoteEventMetadata GetRequiredMetadata(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException(nameof(eventType));
            }

            return registry.FindOrAdd(eventType)
                ?? throw new ArgumentException($"{eventType.FullName} is a local event. Derive it from RemotePubSubEvent<T> to send it to other apps.",nameof(eventType));
        }

        private void EnsureInitialized()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RabbitMQService));
            }

            if (config == null)
            {
                throw new InvalidOperationException("RabbitMQService is not initialized. Call Init first.");
            }
        }

        private static string FormatVirtualHost(string virtualHost)
        {
            return virtualHost == "/" ? "/" : "/" + virtualHost;
        }
    }
}
