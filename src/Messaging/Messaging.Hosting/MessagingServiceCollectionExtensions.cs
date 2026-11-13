using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messaging.Hosting
{
    public static class MessagingServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the buses in <c>Messaging:Buses</c>, publishing through <see cref="IMessagePublisher"/> and receiving
        /// through <see cref="IMessageSubscriber"/>. <c>Messaging:DefaultBus</c> is required and must name one of them.
        /// The buses start after every hosted service has started, so subscribe in a hosted service's <c>StartAsync</c>
        /// or before the host runs.
        /// </summary>
        public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            List<IConfigurationSection> buses = configuration.GetSection("Messaging:Buses").GetChildren().ToList();
            if (buses.Count == 0)
            {
                throw new InvalidOperationException("Messaging:Buses in the configuration has no buses.");
            }

            string defaultBus = configuration["Messaging:DefaultBus"];
            if (string.IsNullOrWhiteSpace(defaultBus))
            {
                throw new ArgumentException("A default bus is required: set Messaging:DefaultBus in the configuration.", nameof(configuration));
            }

            if (!buses.Any(bus => string.Equals(bus.Key, defaultBus, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"The default bus '{defaultBus}' is not in Messaging:Buses.");
            }

            MessagingRegistry registry = new MessagingRegistry(buses.Select(bus => bus.Key), defaultBus);
            services.AddSingleton(registry);
            services.AddSingleton<MessagePublisher>();
            services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<MessagePublisher>());
            services.AddSingleton<MessageSubscriber>();
            services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<MessageSubscriber>());

            foreach (IConfigurationSection bus in buses)
            {
                AddBus(services, bus.Key, bus);
            }

            services.AddHostedService<MessagingHostedService>();
            return services;
        }

        private static void AddBus(IServiceCollection services, string name, IConfiguration section)
        {
            services.AddOptions<RabbitMQBusOptions>(name)
                .Bind(section)
                .PostConfigure(options =>
                {
                    options.BusName = name;
                    if (string.IsNullOrWhiteSpace(options.ClientName))
                    {
                        options.ClientName = Assembly.GetEntryAssembly()?.GetName().Name ?? "messaging";
                    }
                });

            services.AddKeyedSingleton(name, (provider, key) => new RabbitMQBus(
                provider.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get(name),
                provider.GetRequiredService<MessageSubscriber>(),
                provider.GetService<ILogger<RabbitMQBus>>()));
        }
    }
}
