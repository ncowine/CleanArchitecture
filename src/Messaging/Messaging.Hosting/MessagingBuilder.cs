using System;
using System.Reflection;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Hosting
{
    internal sealed class MessagingBuilder
    {
        internal MessagingBuilder(IServiceCollection services, MessagingRegistry registry)
        {
            Services = services;
            Registry = registry;
        }

        public IServiceCollection Services { get; }

        internal MessagingRegistry Registry { get; }

        internal MessagingBuilder AddBus(string name, IConfiguration section)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            AddBus(name);
            Services.AddOptions<RabbitMQBusOptions>(name).Bind(section);
            return this;
        }

        internal MessagingBuilder AddBus(string name, Action<RabbitMQBusOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            AddBus(name);
            Services.AddOptions<RabbitMQBusOptions>(name).Configure(configure);
            return this;
        }

        internal MessagingBuilder UseDefaultBus(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A default bus is required: set Messaging:DefaultBus in the configuration.", nameof(name));
            }

            if (Registry.DefaultBus != null)
            {
                throw new InvalidOperationException($"The default bus is already '{Registry.DefaultBus}'. There can be only one.");
            }

            Registry.DefaultBus = name;
            return this;
        }

        private void AddBus(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A bus name is required.", nameof(name));
            }

            Registry.AddBus(name);

            Services.AddOptions<RabbitMQBusOptions>(name).PostConfigure(options =>
            {
                options.BusName = name;
                if (string.IsNullOrWhiteSpace(options.ClientName))
                {
                    options.ClientName = Assembly.GetEntryAssembly()?.GetName().Name ?? "messaging";
                }
            });

            Services.AddKeyedSingleton(name, (provider, key) => MessagingServiceCollectionExtensions.CreateBus(provider, name));
        }
    }
}
