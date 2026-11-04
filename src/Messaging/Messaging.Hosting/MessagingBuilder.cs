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
    /// Configures messaging in <see cref="MessagingServiceCollectionExtensions.AddMessaging"/>:
    /// <code><![CDATA[
    /// messaging.AddBus("Legacy", configuration.GetSection("Messaging:Buses:Legacy"));
    /// messaging.UseDefaultBus(configuration["Messaging:DefaultBus"]);   // needed with several buses
    /// messaging.AddHandlers(typeof(EmployeeSavedHandler).Assembly);   // every IMessageHandler<T> in it, every bus
    /// messaging.AddHandlers(typeof(OrderSavedHandler).Assembly, "Modern");   // ...or only some buses
    /// messaging.AddMessages(typeof(OrderSaved).Assembly);      // receive every [Message] type in it, wanted or not
    /// messaging.Subscribe("AppA", "AppA.Events.#");           // another application's exchange
    /// messaging.AddTelemetry();                               // server observability; off unless asked for
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

        /// <summary>Adds a bus whose <see cref="RabbitMQBusOptions"/> bind from <paramref name="section"/>.</summary>
        /// <param name="serializer">Null for System.Text.Json (<see cref="SystemTextJsonMessageSerializer"/>).</param>
        public MessagingBuilder AddBus(string name, IConfiguration section, IMessageSerializer serializer = null)
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
        public MessagingBuilder AddBus(string name, Action<RabbitMQBusOptions> configure, IMessageSerializer serializer = null)
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
        /// Picks the bus messages go to when the publisher names none, usually <c>Messaging:DefaultBus</c> from configuration.
        /// Null or blank picks nothing: with one bus that bus is the default, and with several a publish must name one.
        /// </summary>
        public MessagingBuilder UseDefaultBus(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return this;
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
        /// <see cref="IMessageSubscriber"/> and the event aggregator bridge don't need it: each binds only what it listens for.
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
        /// Receives from another application's exchange on the application's only bus (ADR 0002, section 2). The exchange
        /// is checked, never declared. No routing keys means one binding per message type the bus receives.
        /// </summary>
        public MessagingBuilder Subscribe(string exchange, params string[] routingKeys)
        {
            if (string.IsNullOrWhiteSpace(exchange))
            {
                throw new ArgumentException("An exchange is required.", nameof(exchange));
            }

            SubscriptionOptions subscription = new SubscriptionOptions { Exchange = exchange };
            subscription.RoutingKeys.AddRange(routingKeys ?? new string[0]);
            return Subscribe(subscription);
        }

        /// <param name="busName">Null for the application's only bus.</param>
        public MessagingBuilder Subscribe(SubscriptionOptions subscription, string busName = null)
        {
            if (subscription == null || string.IsNullOrWhiteSpace(subscription.Exchange))
            {
                throw new ArgumentException("A subscription with an exchange is required.", nameof(subscription));
            }

            Registry.AddSubscription(new PendingSubscription(subscription, busName));
            return this;
        }

        /// <summary>
        /// Adds tracing, metrics and logs following the OpenTelemetry messaging conventions, and the correlation-id and
        /// W3C trace context headers (ADR 0001, section 5). Off by default so clients get no observability imposed on them.
        /// </summary>
        public MessagingBuilder AddTelemetry()
        {
            Registry.TelemetryEnabled = true;
            return this;
        }

        /// <summary>
        /// Registers every <see cref="IMessageHandler{TMessage}"/> class in <paramref name="assembly"/> as a scoped
        /// handler, resolved in its own DI scope for every message.
        /// </summary>
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
