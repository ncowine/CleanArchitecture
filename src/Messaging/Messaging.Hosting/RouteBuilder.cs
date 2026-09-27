using System;

namespace Messaging.Hosting
{
    /// <summary>
    /// Where <typeparamref name="TMessage"/> is published. Without <see cref="To"/> it goes to the application's only bus;
    /// without <see cref="WithRoutingKey(Func{TMessage, string})"/> it is routed by its wire name.
    /// </summary>
    public sealed class RouteBuilder<TMessage>
    {
        private readonly MessagingBuilder builder;
        private readonly RouteRegistration route;

        internal RouteBuilder(MessagingBuilder builder, RouteRegistration route)
        {
            this.builder = builder;
            this.route = route;
        }

        /// <summary>Publishes the message to every one of <paramref name="busNames"/>.</summary>
        public MessagingBuilder To(params string[] busNames)
        {
            foreach (string busName in MessagingBuilder.CheckBusNames(busNames))
            {
                if (!route.Buses.Contains(busName))
                {
                    route.Buses.Add(busName);
                }
            }

            return builder;
        }

        /// <summary>Routes every message of this type with <paramref name="routingKey"/> (ADR 0002, section 3).</summary>
        public RouteBuilder<TMessage> WithRoutingKey(string routingKey)
        {
            if (string.IsNullOrWhiteSpace(routingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(routingKey));
            }

            route.RoutingKey = message => routingKey;
            return this;
        }

        /// <summary>
        /// Routes each message with a key computed from it, for example <c>o =&gt; $"orders.{o.Region}.saved"</c>.
        /// A null or empty result falls back to the wire name.
        /// </summary>
        public RouteBuilder<TMessage> WithRoutingKey(Func<TMessage, string> routingKey)
        {
            if (routingKey == null)
            {
                throw new ArgumentNullException(nameof(routingKey));
            }

            route.RoutingKey = message => routingKey((TMessage)message);
            return this;
        }

        /// <summary>Ends this route and goes back to the builder, for a route that needs no <see cref="To"/>.</summary>
        public MessagingBuilder And()
        {
            return builder;
        }
    }
}
