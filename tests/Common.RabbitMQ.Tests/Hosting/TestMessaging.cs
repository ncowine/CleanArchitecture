using System;
using System.Collections.Generic;
using Messaging.Hosting;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>
    /// <see cref="MessagingServiceCollectionExtensions.AddMessaging"/> as an app calls it, from configuration, with each
    /// bus's options then set in code: test brokers and unreachable ports aren't known until the test runs.
    /// </summary>
    public static class TestMessaging
    {
        public static IServiceCollection AddTestMessaging(this IServiceCollection services, string defaultBus, params (string Name, Action<RabbitMQBusOptions> Configure)[] buses)
        {
            Dictionary<string, string> settings = new Dictionary<string, string> { ["Messaging:DefaultBus"] = defaultBus };
            foreach ((string name, Action<RabbitMQBusOptions> configure) in buses)
            {
                settings[$"Messaging:Buses:{name}:ExchangeName"] = name.ToLowerInvariant();
            }

            services.AddMessaging(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

            foreach ((string name, Action<RabbitMQBusOptions> configure) in buses)
            {
                services.Configure(name, configure);
            }

            return services;
        }
    }
}
