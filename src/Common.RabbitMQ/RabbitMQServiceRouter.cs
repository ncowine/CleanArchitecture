using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// The application's <see cref="IRabbitMQService"/>. Holds one <see cref="RabbitMQService"/> per configured bus
    /// and sends each event to the bus named in its <see cref="RemoteEventAttribute"/>.
    /// </summary>
    public class RabbitMQServiceRouter : IRabbitMQService, IRoutingKeyPublisher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;
        private readonly Dictionary<string, RabbitMQService> buses = new Dictionary<string, RabbitMQService>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RabbitMQConfig> configs = new Dictionary<string, RabbitMQConfig>(StringComparer.OrdinalIgnoreCase);
        private bool initialized;

        public RabbitMQServiceRouter(IEventAggregator eventAggregator, RemoteEventRegistry registry)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>Log messages from every bus. The sender is the <see cref="RabbitMQService"/>.</summary>
        public event EventHandler<string> Log;

        public IReadOnlyCollection<RabbitMQService> Buses => buses.Values;

        /// <summary>Adds a bus. It connects when <see cref="Init"/> is called.</summary>
        public RabbitMQService AddBus(RabbitMQConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (initialized)
            {
                throw new InvalidOperationException("Buses must be added before Init.");
            }

            if (buses.ContainsKey(config.BusName))
            {
                throw new InvalidOperationException($"Bus '{config.BusName}' is already configured.");
            }

            RabbitMQService service = new RabbitMQService(eventAggregator, registry);
            service.Log += OnBusLog;

            buses.Add(config.BusName, service);
            configs.Add(config.BusName, config);
            return service;
        }

        public RabbitMQService GetBus(string busName)
        {
            return buses.TryGetValue(busName, out RabbitMQService service) ? service : null;
        }

        /// <summary>Initializes every bus (fire and forget - connecting happens in the background).</summary>
        public void Init()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            foreach (KeyValuePair<string, RabbitMQService> pair in buses)
            {
                pair.Value.Init(configs[pair.Key]);
            }
        }

        public void Publish(Type eventType, object payload)
        {
            GetBusFor(eventType).Publish(eventType, payload);
        }

        /// <summary>As <see cref="Publish(Type, object)"/>, routed with <paramref name="routingKey"/>.</summary>
        public void Publish(Type eventType, object payload, string routingKey)
        {
            GetBusFor(eventType).Publish(eventType, payload, routingKey);
        }

        public void Dispose()
        {
            foreach (RabbitMQService service in buses.Values)
            {
                service.Log -= OnBusLog;
                service.Dispose();
            }
        }

        private RabbitMQService GetBusFor(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException(nameof(eventType));
            }

            if (!registry.TryGet(eventType, out RemoteEventDescriptor descriptor))
            {
                throw new ArgumentException($"{eventType.FullName} is not a remote event. Mark it with [RemoteEvent].", nameof(eventType));
            }

            if (descriptor.BusName == null)
            {
                // Unassigned (RemoteEventRegistry.Add without a bus): fine with one bus, ambiguous with several.
                if (buses.Count == 1)
                {
                    return buses.Values.Single();
                }

                throw new InvalidOperationException(
                    $"{eventType.Name} has no bus and this application has {buses.Count}. Pass the bus to RemoteEventRegistry.Add for its assembly.");
            }

            if (!buses.TryGetValue(descriptor.BusName, out RabbitMQService service))
            {
                throw new InvalidOperationException(
                    $"{eventType.Name} belongs to bus '{descriptor.BusName}', which is not configured for this application.");
            }

            return service;
        }

        private void OnBusLog(object sender, string message)
        {
            Log?.Invoke(sender, message);
        }
    }
}
