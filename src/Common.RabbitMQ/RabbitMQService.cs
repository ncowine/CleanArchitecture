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
    /// <summary>
    /// One connection to one bus (virtual host + exchange), for Prism applications.
    /// <para>
    /// A façade over <see cref="RabbitMQBus"/> (ADR 0001): <see cref="Init"/> starts the bus bound to the events the
    /// application subscribes to, <see cref="Publish"/> buffers a message for it, and received messages are raised as their
    /// Prism event on the <see cref="IEventAggregator"/>. Bodies are Newtonsoft JSON, as the legacy apps expect.
    /// </para>
    /// </summary>
    public class RabbitMQService : IRabbitMQService, IRoutingKeyPublisher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;
        private readonly string instanceId = Guid.NewGuid().ToString("N");
        private readonly ConcurrentDictionary<string, bool> registeredQueues = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        private RabbitMQConfig config;
        private RabbitMQBus bus;
        private bool disposed;

        public RabbitMQService(IEventAggregator eventAggregator, RemoteEventRegistry registry)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Receives connection and error messages. Set it before <see cref="Init"/>. Null writes them to Trace. A property,
        /// not a constructor argument: DryIoc rejects types with several public constructors (ADR 0001).
        /// </summary>
        public ILogger Logger { get; set; }

        public string BusName => config?.BusName;

        /// <summary>
        /// Says which bus a subscription's routing key is bound on, or throws when that can't be decided. A service on its
        /// own takes every key not meant for another bus; <see cref="RabbitMQServiceRouter"/> sets it to choose among its buses.
        /// </summary>
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

            ILogger logger = Logger ?? TraceLogger.Instance;

            bus = new RabbitMQBus(CreateOptions(config), NewtonsoftMessageSerializer.Instance, new PrismDispatcher(eventAggregator, registry), logger);

            int subscribedEvents = RemoteEventSubscriptions.Join(this);

            List<MessageRegistration> registrations = registry.BoundAtStart
                .Select(d => new MessageRegistration(d.EventName, d.PayloadType))
                .ToList();

            bus.Start(registrations);

            logger.LogInformation("[{Bus}] Initialized with {Count} event(s) on {Host}:{Port}{VirtualHost} exchange '{Exchange}'.",
                config.BusName, registrations.Count + subscribedEvents, config.HostName, config.Port, FormatVirtualHost(config.VirtualHost), config.ExchangeName);
        }

        /// <summary>
        /// Queues <paramref name="payload"/> as <paramref name="eventType"/>, routed with the exchange name. A key of its own
        /// is chosen when publishing, with <see cref="Publish(Type, object, string)"/>.
        /// </summary>
        public void Publish(Type eventType, object payload)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptor(eventType);
            bus.Enqueue(descriptor.EventName, payload);
        }

        /// <summary>
        /// As <see cref="Publish(Type, object)"/>, routed with <paramref name="routingKey"/>. The event-type header still
        /// carries the full name, so every receiver identifies the event; only subscribers that bind the key receive it.
        /// </summary>
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

        /// <summary>
        /// Starts receiving <paramref name="eventType"/> from the bus, until <see cref="UnregisterQueue"/>, whether or not
        /// anything is subscribed. Subscribing to the event does this for as long as it has subscribers.
        /// </summary>
        public Task RegisterQueue(Type eventType)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptor(eventType);
            registeredQueues[descriptor.EventName] = true;
            bus.Subscribe(new MessageRegistration(descriptor.EventName, descriptor.PayloadType));
            return Task.CompletedTask;
        }

        /// <summary>Stops receiving <paramref name="eventType"/> from the bus. Publishing is not affected.</summary>
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

        /// <summary>Starts receiving <paramref name="remoteEvent"/> if it belongs to this service's aggregator, and says whether it does.</summary>
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

        /// <summary>Binds a subscription's routing key, if this is the default bus and the event belongs to its aggregator.</summary>
        internal void Bind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            if (FindOwn(remoteEvent) != null && IsBindingBus(options))
            {
                bus.Bind(options.Exchange, options.RoutingKey);
            }
        }

        /// <summary>As <see cref="Bind"/>, but a key that can't be placed is logged instead of thrown: for subscriptions made before Init.</summary>
        internal void TryBind(IRemoteEvent remoteEvent, SubscribeOptions options)
        {
            try
            {
                Bind(remoteEvent, options);
            }
            catch (InvalidOperationException ex)
            {
                (Logger ?? TraceLogger.Instance).LogError(ex, "[{Bus}] Routing key '{RoutingKey}' is not bound: {Error}", config.BusName, options.RoutingKey, ex.Message);
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
                // Never bound, for the same reason.
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

        /// <summary>Stops receiving <paramref name="remoteEvent"/> if it belongs to this service's aggregator, unless <see cref="RegisterQueue"/> asked for it.</summary>
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
