using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// The application's <see cref="IRabbitMQService"/>. Holds one <see cref="RabbitMQService"/> per configured bus and
    /// sends every event to the default bus. To send one elsewhere, pass <see cref="GetRequiredBus"/> to <c>PublishRemote</c>.
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

        /// <summary>
        /// Builds a router from the <c>Messaging</c> section of <paramref name="configuration"/>: one bus per entry in
        /// <c>Buses</c>, the default from <c>DefaultBus</c>, and every <see cref="PubSubEvent{TPayload}"/> in
        /// <paramref name="eventAssemblies"/>. Call <see cref="Init"/> once the application has subscribed.
        /// </summary>
        public static RabbitMQServiceRouter FromConfiguration(IEventAggregator eventAggregator, IConfiguration configuration, params Assembly[] eventAssemblies)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            RemoteEventRegistry registry = new RemoteEventRegistry();
            foreach (Assembly assembly in eventAssemblies)
            {
                registry.Add(assembly);
            }

            RabbitMQServiceRouter router = new RabbitMQServiceRouter(eventAggregator, registry)
            {
                DefaultBus = configuration["Messaging:DefaultBus"],
            };

            foreach (IConfigurationSection bus in configuration.GetSection("Messaging:Buses").GetChildren())
            {
                RabbitMQConfig config = bus.Get<RabbitMQConfig>() ?? new RabbitMQConfig();
                config.BusName = bus.Key;
                router.AddBus(config);
            }

            if (router.buses.Count == 0)
            {
                throw new InvalidOperationException("Messaging:Buses in the configuration has no buses.");
            }

            return router;
        }

        /// <summary>Receives messages from every bus. Set it before <see cref="Init"/>. Null writes them to Trace.</summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// The bus events go to when the publisher names none, usually <c>Messaging:DefaultBus</c> from configuration. Set it
        /// before <see cref="Init"/>. Null with one bus means that bus; null with several means a publish must name one.
        /// </summary>
        public string DefaultBus { get; set; }

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

            buses.Add(config.BusName, service);
            configs.Add(config.BusName, config);
            return service;
        }

        public RabbitMQService GetBus(string busName)
        {
            return buses.TryGetValue(busName, out RabbitMQService service) ? service : null;
        }

        /// <summary>As <see cref="GetBus"/>, but an unknown name throws, so a mistyped bus can't fall back to the default.</summary>
        public RabbitMQService GetRequiredBus(string busName)
        {
            if (busName == null)
            {
                throw new ArgumentNullException(nameof(busName));
            }

            return GetBus(busName) ?? throw new InvalidOperationException($"Bus '{busName}' is not configured for this application.");
        }

        /// <summary>Initializes every bus (fire and forget - connecting happens in the background).</summary>
        public void Init()
        {
            if (initialized)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(DefaultBus) && !buses.ContainsKey(DefaultBus))
            {
                throw new InvalidOperationException($"The default bus '{DefaultBus}' is not configured for this application.");
            }

            initialized = true;

            foreach (KeyValuePair<string, RabbitMQService> pair in buses)
            {
                pair.Value.Logger = Logger;
                pair.Value.Init(configs[pair.Key]);
            }
        }

        public void Publish(Type eventType, object payload)
        {
            GetDefaultBus(eventType).Publish(eventType, payload);
        }

        /// <summary>As <see cref="Publish(Type, object)"/>, routed with <paramref name="routingKey"/>.</summary>
        public void Publish(Type eventType, object payload, string routingKey)
        {
            GetDefaultBus(eventType).Publish(eventType, payload, routingKey);
        }

        public void Dispose()
        {
            foreach (RabbitMQService service in buses.Values)
            {
                service.Dispose();
            }
        }

        private RabbitMQService GetDefaultBus(Type eventType)
        {
            if (eventType == null)
            {
                throw new ArgumentNullException(nameof(eventType));
            }

            if (!string.IsNullOrWhiteSpace(DefaultBus))
            {
                return GetRequiredBus(DefaultBus);
            }

            if (buses.Count == 1)
            {
                return buses.Values.Single();
            }

            throw new InvalidOperationException(
                $"Cannot publish {eventType.Name}: this application has {buses.Count} buses and no default. Set Messaging:DefaultBus, or publish to a bus from GetRequiredBus.");
        }
    }
}
