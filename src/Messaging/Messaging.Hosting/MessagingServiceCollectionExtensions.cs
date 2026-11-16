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
                throw new InvalidOperationException("No buses in Messaging:Buses.");
            }

            string defaultBus = configuration["Messaging:DefaultBus"];
            if (string.IsNullOrWhiteSpace(defaultBus))
            {
                throw new ArgumentException("No Messaging:DefaultBus configured.", nameof(configuration));
            }

            if (!buses.Any(bus => string.Equals(bus.Key, defaultBus, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Default bus '{defaultBus}' is not in Messaging:Buses.");
            }

            MessagingRegistry registry = new MessagingRegistry(buses.Select(bus => bus.Key), defaultBus);
            services.AddLogging();
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
                provider.GetRequiredService<ILogger<RabbitMQBus>>()));
        }
    }
}
