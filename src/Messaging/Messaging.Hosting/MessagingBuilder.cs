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
    /// <summary>
    /// Configures messaging in <see cref="MessagingServiceCollectionExtensions.AddMessaging(IServiceCollection, IConfiguration, Action{MessagingBuilder})"/>,
    /// which has already added the buses and the default from configuration:
    /// <code><![CDATA[
    /// messaging.AddHandlers(typeof(EmployeeSavedHandler).Assembly);   // every IMessageHandler<T> in it, every bus
    /// messaging.AddHandlers(typeof(OrderSavedHandler).Assembly, "Modern");   // ...or only some buses
    /// messaging.AddMessages(typeof(OrderSaved).Assembly);      // receive every [Message] type in it, wanted or not
    /// messaging.Subscribe("AppA", "AppA.Events.#");           // another application's exchange
    /// ]]></code>
    /// </summary>
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

        /// <param name="serializer">Null for System.Text.Json (<see cref="SystemTextJsonMessageSerializer"/>).</param>
        internal MessagingBuilder AddBus(string name, IConfiguration section, IMessageSerializer serializer = null)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            AddBus(name, serializer);
            Services.AddOptions<RabbitMQBusOptions>(name).Bind(section);
            return this;
        }

        /// <param name="serializer">Null for System.Text.Json (<see cref="SystemTextJsonMessageSerializer"/>).</param>
        internal MessagingBuilder AddBus(string name, Action<RabbitMQBusOptions> configure, IMessageSerializer serializer = null)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            AddBus(name, serializer);
            Services.AddOptions<RabbitMQBusOptions>(name).Configure(configure);
            return this;
        }

        /// <summary>
        /// Picks the bus messages go to when the publisher names none: <c>Messaging:DefaultBus</c> from configuration.
        /// Required, even with one bus: the buses don't start without it.
        /// </summary>
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

        /// <summary>
        /// Receives every <see cref="MessageAttribute"/> type in <paramref name="assembly"/>, whether or not anything
        /// listens for it, so a custom <see cref="IMessageSink"/> gets them (ADR 0002, section 5). Handlers,
        /// <see cref="IMessageSubscriber"/> and the event aggregator bridge don't need it: each receives what it listens for.
        /// </summary>
        /// <param name="busNames">The buses to receive them on. None means every bus.</param>
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

        /// <summary>
        /// Receives from another application's exchange on every bus (ADR 0002, section 2), bound with the exchange's name, as
        /// its owner publishes. The exchange is checked, never declared. Other routing keys are bound by subscribing with
        /// <see cref="SubscribeOptions"/>.
        /// </summary>
        public MessagingBuilder Subscribe(string exchange)
        {
            if (string.IsNullOrWhiteSpace(exchange))
            {
                throw new ArgumentException("An exchange is required.", nameof(exchange));
            }

            return Subscribe(new SubscriptionOptions { Exchange = exchange });
        }

        /// <param name="busName">Null for every bus.</param>
        public MessagingBuilder Subscribe(SubscriptionOptions subscription, string busName = null)
        {
            if (subscription == null || string.IsNullOrWhiteSpace(subscription.Exchange))
            {
                throw new ArgumentException("A subscription with an exchange is required.", nameof(subscription));
            }

            Registry.AddSubscription(new PendingSubscription(subscription, busName));
            return this;
        }

        /// <param name="busNames">The buses they receive from. None means every bus.</param>
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

        private void AddBus(string name, IMessageSerializer serializer)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A bus name is required.", nameof(name));
            }

            Registry.AddBus(name, serializer);

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
