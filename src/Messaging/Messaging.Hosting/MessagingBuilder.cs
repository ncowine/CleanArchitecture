using System;
using System.Linq;
using System.Reflection;
using Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Hosting
{
    /// <summary>
    /// Configures messaging in <see cref="MessagingServiceCollectionExtensions.AddMessaging"/>:
    /// <code>
    /// messaging.AddBus("Legacy", configuration.GetSection("Messaging:Buses:Legacy"));
    /// messaging.Route&lt;EmployeeUpdated&gt;().To("Legacy", "Modern");
    /// messaging.Route&lt;OrderSaved&gt;().WithRoutingKey(o =&gt; $"orders.{o.Region}.saved");   // the only bus
    /// messaging.Handle&lt;EmployeeSaved, EmployeeSavedHandler&gt;().From("Modern");
    /// messaging.AddMessages(typeof(OrderSaved).Assembly);      // receive every [Message] type in it, e.g. for a sink
    /// messaging.Subscribe("AppA", "AppA.Events.#");           // another application's exchange
    /// messaging.AddTelemetry();                               // server observability; off unless asked for
    /// </code>
    /// </summary>
    public sealed class MessagingBuilder
    {
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

        /// <summary>Where, and with which routing key, <typeparamref name="TMessage"/> is published.</summary>
        public RouteBuilder<TMessage> Route<TMessage>()
        {
            return new RouteBuilder<TMessage>(this, Registry.Route(typeof(TMessage)));
        }

        /// <summary>
        /// Receives every <see cref="MessageAttribute"/> type in <paramref name="assembly"/>, with or without a handler, so
        /// an <see cref="IMessageSink"/> such as the event aggregator bridge gets them (ADR 0002, section 5).
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
        /// Registers <typeparamref name="THandler"/> as a scoped handler. Choose the buses it receives from with
        /// <see cref="HandlerBuilder.From"/>.
        /// </summary>
        public HandlerBuilder Handle<TMessage, THandler>()
            where THandler : class, IMessageHandler<TMessage>
        {
            Services.AddScoped<THandler>();
            return new HandlerBuilder(this, typeof(TMessage), typeof(THandler));
        }

        private void AddBus(string name, IMessageSerializer serializer)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A bus name is required.", nameof(name));
            }

            Registry.AddBus(new BusRegistration(name, serializer));

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

        internal static string[] CheckBusNames(string[] busNames)
        {
            if (busNames == null || busNames.Length == 0 || busNames.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("At least one bus name is required, and none may be empty.", nameof(busNames));
            }

            return busNames;
        }
    }
}
