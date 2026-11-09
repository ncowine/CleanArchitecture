using System;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messaging.Hosting
{
    public static class MessagingServiceCollectionExtensions
    {
        public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration, Action<MessagingBuilder> configure = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return services.AddMessaging(messaging =>
            {
                foreach (IConfigurationSection bus in configuration.GetSection("Messaging:Buses").GetChildren())
                {
                    messaging.AddBus(bus.Key, bus);
                }

                if (messaging.Registry.BusNames.Count == 0)
                {
                    throw new InvalidOperationException("Messaging:Buses in the configuration has no buses.");
                }

                messaging.UseDefaultBus(configuration["Messaging:DefaultBus"]);
                configure?.Invoke(messaging);
            });
        }

        internal static IServiceCollection AddMessaging(this IServiceCollection services, Action<MessagingBuilder> configure)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            MessagingRegistry registry = new MessagingRegistry();
            services.AddSingleton(registry);
            services.AddSingleton<MessagePublisher>();
            services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<MessagePublisher>());
            services.AddSingleton<MessageSubscriber>();
            services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<MessageSubscriber>());
            services.AddSingleton<IMessageSink>(provider => provider.GetRequiredService<MessageSubscriber>());

            configure(new MessagingBuilder(services, registry));

            services.AddHostedService<MessagingHostedService>();
            return services;
        }

        internal static RabbitMQBus CreateBus(IServiceProvider provider, string name)
        {
            MessagingRegistry registry = provider.GetRequiredService<MessagingRegistry>();
            RabbitMQBusOptions options = provider.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get(name);

            options.Subscriptions.AddRange(registry.GetSubscriptions(name));

            return new RabbitMQBus(
                options,
                new ScopedDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), registry, name),
                provider.GetService<ILogger<RabbitMQBus>>());
        }
    }
}
