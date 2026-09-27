using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// One connection to one bus (virtual host + exchange), for Prism applications.
    /// <para>
    /// A façade over <see cref="RabbitMQBus"/> (ADR 0001): <see cref="Init"/> starts the bus with every remote event
    /// of this bus subscribed, <see cref="Publish"/> buffers a message for it, and received messages are raised as their
    /// Prism event on the <see cref="IEventAggregator"/>. Bodies are Newtonsoft JSON, as the legacy apps expect.
    /// </para>
    /// </summary>
    public class RabbitMQService : IRabbitMQService, IRoutingKeyPublisher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;
        private readonly string instanceId = Guid.NewGuid().ToString("N");

        private RabbitMQConfig config;
        private RabbitMQBus bus;
        private bool disposed;

        public RabbitMQService(IEventAggregator eventAggregator, RemoteEventRegistry registry)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>Connection and error messages. The sender is this service.</summary>
        public event EventHandler<string> Log;

        public string BusName => config?.BusName;

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

            List<MessageRegistration> registrations = registry.GetByBus(config.BusName)
                .Select(d => new MessageRegistration(d.EventName, d.PayloadType))
                .ToList();

            bus = new RabbitMQBus(CreateOptions(config), NewtonsoftMessageSerializer.Instance, new PrismDispatcher(eventAggregator, registry));
            bus.Log += OnBusLog;
            bus.Start(registrations);

            WriteLog($"Initialized with {registrations.Count} event(s) on {config.HostName}:{config.Port}{FormatVirtualHost(config.VirtualHost)} exchange '{config.ExchangeName}'.");
        }

        /// <summary>
        /// Queues <paramref name="payload"/> as <paramref name="eventType"/>, routed with the event's full name or with its
        /// entry in <see cref="RabbitMQConfig.RoutingKeys"/>.
        /// </summary>
        public void Publish(Type eventType, object payload)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptorForThisBus(eventType);
            config.RoutingKeys.TryGetValue(descriptor.EventName, out string routingKey);
            bus.Enqueue(descriptor.EventName, payload, routingKey);
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

            RemoteEventDescriptor descriptor = GetDescriptorForThisBus(eventType);
            bus.Enqueue(descriptor.EventName, payload, routingKey);
        }

        /// <summary>
        /// Starts receiving <paramref name="eventType"/> from the bus. All events of this bus are registered by
        /// <see cref="Init"/>; use this after <see cref="UnregisterQueue"/> to resume.
        /// </summary>
        public async Task RegisterQueue(Type eventType)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptorForThisBus(eventType);
            await bus.Subscribe(new MessageRegistration(descriptor.EventName, descriptor.PayloadType)).ConfigureAwait(false);
        }

        /// <summary>Stops receiving <paramref name="eventType"/> from the bus. Publishing is not affected.</summary>
        public async Task UnregisterQueue(Type eventType)
        {
            EnsureInitialized();

            RemoteEventDescriptor descriptor = GetDescriptorForThisBus(eventType);
            await bus.Unsubscribe(descriptor.EventName).ConfigureAwait(false);
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
                    .Select(s => new SubscriptionOptions { Exchange = s.Exchange, RoutingKeys = new List<string>(s.RoutingKeys) })
                    .ToList(),
            };
        }

        private RemoteEventDescriptor GetDescriptorForThisBus(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException(nameof(eventType));
            }

            if (!registry.TryGet(eventType, out RemoteEventDescriptor descriptor))
            {
                throw new ArgumentException($"{eventType.FullName} is not a remote event. Mark it with [RemoteEvent].", nameof(eventType));
            }

            // Unassigned events (RemoteEventRegistry.Add without a bus) belong to every bus.
            if (descriptor.BusName != null && !string.Equals(descriptor.BusName, config.BusName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{eventType.Name} belongs to bus '{descriptor.BusName}', not '{config.BusName}'.");
            }

            return descriptor;
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

        private void OnBusLog(object sender, string message)
        {
            WriteLog(message);
        }

        private void WriteLog(string message)
        {
            Trace.WriteLine($"[RabbitMQ:{BusName}] {message}");

            // Applications cast the sender to RabbitMQService, so it must be this façade, not the bus.
            Log?.Invoke(this, message);
        }

        private static string FormatVirtualHost(string virtualHost)
        {
            return virtualHost == "/" ? "/" : "/" + virtualHost;
        }
    }
}
