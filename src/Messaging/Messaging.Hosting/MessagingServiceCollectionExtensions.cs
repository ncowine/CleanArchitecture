using System;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messaging.Hosting
{
    public static class MessagingServiceCollectionExtensions
    {
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
            services.AddSingleton<MessagePublisher>();
            services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<MessagePublisher>());
            services.AddSingleton<MessageSubscriber>();
            services.AddSingleton<IMessageSubscriber>(provider => provider.GetRequiredService<MessageSubscriber>());
            services.AddSingleton<IMessageSink>(provider => provider.GetRequiredService<MessageSubscriber>());

            configure(new MessagingBuilder(services, registry));

            // Last, so what configure adds starts first: the event aggregator bridge tells the buses what was already
            // subscribed before they connect, or a durable queue's backlog would look unknown.
            services.AddHostedService<MessagingHostedService>();
            return services;
        }

        internal static RabbitMQBus CreateBus(IServiceProvider provider, string name)
        {
            MessagingRegistry registry = provider.GetRequiredService<MessagingRegistry>();
            RabbitMQBusOptions options = provider.GetRequiredService<IOptionsMonitor<RabbitMQBusOptions>>().Get(name);
            IMessageSerializer serializer = registry.GetSerializer(name) ?? new SystemTextJsonMessageSerializer();

            // Subscriptions added in code join those from configuration. Each bus is created once.
            options.Subscriptions.AddRange(registry.GetSubscriptions(name));

            return new RabbitMQBus(
                options,
                serializer,
                new ScopedDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), registry, name),
                provider.GetService<ILogger<RabbitMQBus>>());
        }
    }
}
