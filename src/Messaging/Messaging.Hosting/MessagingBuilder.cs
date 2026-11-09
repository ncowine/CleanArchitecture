using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Messaging.Hosting
{
    public sealed class MessagingBuilder
    {
        private static readonly MethodInfo AddHandlerMethod =
            typeof(MessagingBuilder).GetMethod(nameof(AddHandler), BindingFlags.NonPublic | BindingFlags.Instance);

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

        public MessagingBuilder AddMessages(Assembly assembly, params string[] busNames)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            foreach (Type type in assembly.GetTypes().Where(t => !t.IsAbstract && t.GetCustomAttribute<MessageAttribute>(inherit: false) != null))
            {
                Registry.AddMessageType(new MessageTypeRegistration(type, busNames ?? new string[0]));
            }

            return this;
        }

        public MessagingBuilder Subscribe(string exchange)
        {
            if (string.IsNullOrWhiteSpace(exchange))
            {
                throw new ArgumentException("An exchange is required.", nameof(exchange));
            }

            return Subscribe(new SubscriptionOptions { Exchange = exchange });
        }

        public MessagingBuilder Subscribe(SubscriptionOptions subscription, string busName = null)
        {
            if (subscription == null || string.IsNullOrWhiteSpace(subscription.Exchange))
            {
                throw new ArgumentException("A subscription with an exchange is required.", nameof(subscription));
            }

            Registry.AddSubscription(new PendingSubscription(subscription, busName));
            return this;
        }

        public MessagingBuilder AddHandlers(Assembly assembly, params string[] busNames)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            foreach (Type handlerType in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters))
            {
                foreach (Type handlerInterface in handlerType.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageHandler<>)))
                {
                    AddHandlerMethod
                        .MakeGenericMethod(handlerInterface.GetGenericArguments()[0], handlerType)
                        .Invoke(this, new object[] { busNames ?? new string[0] });
                }
            }

            return this;
        }

        private void AddHandler<TMessage, THandler>(string[] busNames)
            where THandler : class, IMessageHandler<TMessage>
        {
            Services.TryAddScoped<THandler>();
            Registry.AddHandler(new HandlerRegistration(typeof(TMessage), typeof(THandler), busNames, Invoke<TMessage, THandler>));
        }

        private static Task Invoke<TMessage, THandler>(object handler, object message, MessageContext context, CancellationToken cancellationToken)
            where THandler : IMessageHandler<TMessage>
        {
            return ((THandler)handler).Handle((TMessage)message, context, cancellationToken);
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
