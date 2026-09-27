using System;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Messaging.Hosting
{
    public static class MessagingServiceCollectionExtensions
    {
        /// <summary>
        /// Adds messaging: one keyed <see cref="RabbitMQBus"/> per bus, <see cref="IMessagePublisher"/>,
        /// <see cref="IMessageSubscriber"/>, scoped async handlers, and a hosted service that starts and stops the buses. Telemetry is added with
        /// <see cref="MessagingBuilder.AddTelemetry"/>.
        /// </summary>
        public static IServiceCollection AddMessaging(this IServiceCollection services, Action<MessagingBuilder> configure)
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
            services.AddSingleton<TelemetryObserver>();
            services.AddSingleton<IMessagePublisher, MessagePublisher>();
            services.AddSingleton<MessageSubscriber>();
            services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<MessageSubscriber>());
            services.AddSingleton<IMessageSink>(provider => provider.GetRequiredService<MessageSubscriber>());
            services.AddHostedService<MessagingHostedService>();

            configure(new MessagingBuilder(services, registry));
            return services;
        }

        internal static RabbitMQBus CreateBus(IServiceProvider provider, string name)
        {
            MessagingRegistry registry = provider.GetRequiredService<MessagingRegistry>();
            RabbitMQBusOptions options = provider.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get(name);
            IMessageSerializer serializer = registry.GetBus(name).Serializer ?? new SystemTextJsonMessageSerializer();

            // Subscriptions added in code join those from configuration. Each bus is created once.
            options.Subscriptions.AddRange(registry.GetSubscriptions(name));

            return new RabbitMQBus(
                options,
                serializer,
                new ScopedDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), registry, name),
                registry.TelemetryEnabled ? provider.GetRequiredService<TelemetryObserver>() : null);
        }
    }
}
