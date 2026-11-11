using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Messaging;
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
        private readonly ConcurrentDictionary<string, bool> registeredQueues = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private RabbitMQConfig config;
        private RabbitMQBus bus;
        private bool disposed;

        public RabbitMQService(IEventAggregator eventAggregator, RemoteEventRegistry registry, ILogger logger)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string BusName => config?.BusName;

        internal Func<SubscribeOptions, string> BindingBus { get; set; }

        public string InstanceId => instanceId;

        public bool IsConsumerConnected => bus != null && bus.IsConsumerConnected;

        public bool IsPublisherConnected => bus != null && bus.IsPublisherConnected;

        public int OutstandingCount => bus?.OutstandingCount ?? 0;

        public void Init(RabbitMQConfig config)
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

            bus = new RabbitMQBus(CreateOptions(config), new PrismDispatcher(eventAggregator, registry), logger);

            int subscribedEvents = RemoteEventSubscriptions.Join(this);

            bus.Start(new List<MessageRegistration>());

            logger.LogInformation("[{Bus}] Initialized with {Count} event(s) on {Host}:{Port}{VirtualHost} exchange '{Exchange}'.",
                config.BusName, subscribedEvents, config.HostName, config.Port, FormatVirtualHost(config.VirtualHost), config.ExchangeName);
        }

        public void Publish(Type eventType, object payload)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptor(eventType);
            bus.Enqueue(descriptor.EventName, payload);
        }

        public void Publish(Type eventType, object payload, string routingKey)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(routingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(routingKey));
            }

            RemoteEventDescriptor descriptor = GetDescriptor(eventType);
            bus.Enqueue(descriptor.EventName, payload, routingKey);
        }

        public Task RegisterQueue(Type eventType)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptor(eventType);
            registeredQueues[descriptor.EventName] = true;
            bus.Subscribe(new MessageRegistration(descriptor.EventName, descriptor.PayloadType));
            return Task.CompletedTask;
        }

        public Task UnregisterQueue(Type eventType)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptor(eventType);
            registeredQueues.TryRemove(descriptor.EventName, out _);
            bus.Unsubscribe(descriptor.EventName);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            RemoteEventSubscriptions.Leave(this);
            bus?.Dispose();
        }

        internal bool Receive(IRemoteEvent remoteEvent)
        {
            RemoteEventDescriptor descriptor = FindOwn(remoteEvent);
            if (descriptor == null)
            {
                return false;
            }

            bus.Subscribe(new MessageRegistration(descriptor.EventName, descriptor.PayloadType));
            return true;
        }

        internal void Bind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            if (FindOwn(remoteEvent) != null && IsBindingBus(options))
            {
                bus.Bind(options.Exchange, options.RoutingKey);
            }
        }

        internal void TryBind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            try
            {
                Bind(remoteEvent, options);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "[{Bus}] Routing key '{RoutingKey}' is not bound: {Error}", config.BusName, options.RoutingKey, ex.Message);
            }
        }

        internal void Unbind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            if (FindOwn(remoteEvent) == null)
            {
                return;
            }

            bool mine;
            try
            {
                mine = IsBindingBus(options);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (mine)
            {
                bus.Unbind(options.Exchange, options.RoutingKey);
            }
        }

        private bool IsBindingBus(SubscribeOptions options)
        {
            if (BindingBus != null)
            {
                return BindingBus(options) == config.BusName;
            }

            if (!string.IsNullOrWhiteSpace(options.Bus) && options.Bus != config.BusName)
            {
                throw new InvalidOperationException($"Cannot bind a routing key on bus '{options.Bus}': it is not configured for this application.");
            }

            return true;
        }

        internal void StopReceiving(IRemoteEvent remoteEvent)
        {
            RemoteEventDescriptor descriptor = FindOwn(remoteEvent);
            if (descriptor != null && !registeredQueues.ContainsKey(descriptor.EventName))
            {
                bus.Unsubscribe(descriptor.EventName);
            }
        }

        private RemoteEventDescriptor FindOwn(IRemoteEvent remoteEvent)
        {
            RemoteEventDescriptor descriptor = registry.Find(remoteEvent.GetType());
            return ReferenceEquals(descriptor?.GetEvent(eventAggregator), remoteEvent) ? descriptor : null;
        }

        private RabbitMQBusOptions CreateOptions(RabbitMQConfig config)
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
                Subscriptions = config.Subscriptions
                    .Where(s => s != null)
                    .Select(s => new SubscriptionOptions { Exchange = s.Exchange })
                    .ToList(),
            };
        }

        private RemoteEventDescriptor GetDescriptor(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException(nameof(eventType));
            }

            return registry.Find(eventType)
                ?? throw new ArgumentException($"{eventType.FullName} is a local event. Derive it from RemotePubSubEvent<T>, or add its assembly with RemoteEventRegistry.Add.", nameof(eventType));
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
