using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Prism.Events;

namespace Common.RabbitMQ
{
    public class RabbitMQServiceRouter : IRabbitMQService, IRoutingKeyPublisher
    {
        private readonly IEventAggregator eventAggregator;
        private readonly RemoteEventRegistry registry;
        private readonly ILogger logger;
        private readonly Dictionary<string, RabbitMQService> buses = new Dictionary<string, RabbitMQService>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RabbitMQConfig> configs = new Dictionary<string, RabbitMQConfig>(StringComparer.OrdinalIgnoreCase);
        private bool initialized;

        internal RabbitMQServiceRouter(IEventAggregator eventAggregator, RemoteEventRegistry registry, ILogger logger)
        {
            this.eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public static RabbitMQServiceRouter FromConfiguration(IEventAggregator eventAggregator, IConfiguration configuration, ILogger logger)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            RabbitMQServiceRouter router = new RabbitMQServiceRouter(eventAggregator, new RemoteEventRegistry(), logger)
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

        public string DefaultBus { get; internal set; }

        public IReadOnlyCollection<RabbitMQService> Buses => buses.Values;

        internal RabbitMQService AddBus(RabbitMQConfig config)
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

            RabbitMQService service = new RabbitMQService(eventAggregator, registry, logger);

            buses.Add(config.BusName, service);
            configs.Add(config.BusName, config);
            return service;
        }

        public RabbitMQService GetBus(string busName)
        {
            return buses.TryGetValue(busName, out RabbitMQService service) ? service : null;
        }

        public RabbitMQService GetRequiredBus(string busName)
        {
            if (busName == null)
            {
                throw new ArgumentNullException(nameof(busName));
            }

            return GetBus(busName) ?? throw new InvalidOperationException($"Bus '{busName}' is not configured for this application.");
        }

        public void Init()
        {
            if (initialized)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(DefaultBus))
            {
                throw new InvalidOperationException(NoDefaultBus);
            }

            if (!buses.ContainsKey(DefaultBus))
            {
                throw new InvalidOperationException($"The default bus '{DefaultBus}' is not configured for this application.");
            }

            initialized = true;

            foreach (KeyValuePair<string, RabbitMQService> pair in buses)
            {
                pair.Value.BindingBus = BusForBinding;
                pair.Value.Init(configs[pair.Key]);
            }
        }

        internal string BusForBinding(SubscribeOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.Bus))
            {
                return buses.ContainsKey(options.Bus)
                    ? options.Bus
                    : throw new InvalidOperationException($"Cannot bind a routing key on bus '{options.Bus}': it is not configured for this application.");
            }

            if (!string.IsNullOrWhiteSpace(options.Exchange))
            {
                List<string> matches = configs
                    .Where(c => c.Value.ExchangeName == options.Exchange || c.Value.Subscriptions.Any(s => s != null && s.Exchange == options.Exchange))
                    .Select(c => c.Key)
                    .ToList();

                if (matches.Count == 1)
                {
                    return matches[0];
                }

                if (matches.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Exchange '{options.Exchange}' is on several buses ({string.Join(", ", matches)}): set SubscribeOptions.Bus to say which.");
                }
            }

            return DefaultBus;
        }

        public void Publish(Type eventType, object payload)
        {
            GetDefaultBus(eventType).Publish(eventType, payload);
        }

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

            if (string.IsNullOrWhiteSpace(DefaultBus))
            {
                throw new InvalidOperationException($"Cannot publish {eventType.Name}: {NoDefaultBus}");
            }

            return GetRequiredBus(DefaultBus);
        }

        private const string NoDefaultBus = "No default bus: set Messaging:DefaultBus in the configuration.";
    }
}
