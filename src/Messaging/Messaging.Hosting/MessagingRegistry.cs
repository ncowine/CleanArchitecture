using System;
using System.Collections.Generic;
using System.Linq;
using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    /// <summary>
    /// What <see cref="MessagingBuilder"/> configured: the buses, where and with which routing key each message type is
    /// published (routes), which message types are received and by which handlers, and subscriptions added in code.
    /// Routing belongs to the host, not the contract (ADR 0001, section 1; ADR 0002, section 5).
    /// </summary>
    internal sealed class MessagingRegistry
    {
        private readonly Dictionary<string, BusRegistration> buses = new Dictionary<string, BusRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<Type, RouteRegistration> routes = new Dictionary<Type, RouteRegistration>();
        private readonly List<HandlerRegistration> handlers = new List<HandlerRegistration>();
        private readonly List<MessageTypeRegistration> messageTypes = new List<MessageTypeRegistration>();
        private readonly List<PendingSubscription> subscriptions = new List<PendingSubscription>();

        public IReadOnlyCollection<BusRegistration> Buses => buses.Values;

        /// <summary>Set by <see cref="MessagingBuilder.AddTelemetry"/>.</summary>
        public bool TelemetryEnabled { get; set; }

        public void AddBus(BusRegistration bus)
        {
            if (buses.ContainsKey(bus.Name))
            {
                throw new InvalidOperationException($"Bus '{bus.Name}' is already added.");
            }

            buses.Add(bus.Name, bus);
        }

        public BusRegistration GetBus(string name)
        {
            return buses[name];
        }

        /// <summary>The route for <paramref name="messageType"/>, created on first use.</summary>
        public RouteRegistration Route(Type messageType)
        {
            if (!routes.TryGetValue(messageType, out RouteRegistration route))
            {
                route = new RouteRegistration(messageType);
                routes.Add(messageType, route);
            }

            return route;
        }

        /// <returns>The route of <paramref name="messageType"/>, or null when it has none.</returns>
        public RouteRegistration GetRoute(Type messageType)
        {
            return routes.TryGetValue(messageType, out RouteRegistration route) ? route : null;
        }

        /// <summary>The buses a route publishes to: its own list, or the only bus when the list is empty.</summary>
        public IReadOnlyList<string> GetRouteBuses(RouteRegistration route)
        {
            return route.Buses.Count > 0 ? route.Buses : buses.Keys.ToList();
        }

        public void AddHandler(HandlerRegistration handler)
        {
            handlers.Add(handler);
        }

        public void AddMessageType(MessageTypeRegistration messageType)
        {
            messageTypes.Add(messageType);
        }

        public void AddSubscription(PendingSubscription subscription)
        {
            subscriptions.Add(subscription);
        }

        public IEnumerable<SubscriptionOptions> GetSubscriptions(string busName)
        {
            return subscriptions
                .Where(s => s.BusName == null || string.Equals(s.BusName, busName, StringComparison.Ordinal))
                .Select(s => s.Subscription);
        }

        public IEnumerable<HandlerRegistration> GetHandlers(Type messageType, string busName)
        {
            return handlers.Where(h => h.MessageType == messageType && h.Buses.Contains(busName, StringComparer.Ordinal));
        }

        /// <summary>
        /// The message types a bus receives: every type with a handler on that bus, plus every type added with
        /// <see cref="MessagingBuilder.AddMessages"/> for that bus.
        /// </summary>
        public IEnumerable<MessageRegistration> GetMessageRegistrations(string busName)
        {
            IEnumerable<Type> handled = handlers
                .Where(h => h.Buses.Contains(busName, StringComparer.Ordinal))
                .Select(h => h.MessageType);
            IEnumerable<Type> added = messageTypes
                .Where(m => m.Buses.Count == 0 || m.Buses.Contains(busName, StringComparer.Ordinal))
                .Select(m => m.MessageType);

            return handled.Concat(added)
                .Distinct()
                .Select(t => new MessageRegistration(WireNames.Get(t), t));
        }

        /// <summary>Fails fast at startup instead of on the first message.</summary>
        public void Validate()
        {
            List<string> errors = new List<string>();

            foreach (RouteRegistration route in routes.Values)
            {
                if (route.Buses.Count == 0 && buses.Count != 1)
                {
                    errors.Add($"{route.MessageType.Name} has no bus and this application has {buses.Count}. Use Route<{route.MessageType.Name}>().To(...).");
                }

                errors.AddRange(UnknownBuses(route.Buses).Select(b => $"{route.MessageType.Name} is routed to unknown bus '{b}'."));
                errors.AddRange(MissingWireName(route.MessageType));
            }

            foreach (HandlerRegistration handler in handlers)
            {
                errors.AddRange(UnknownBuses(handler.Buses).Select(b => $"{handler.HandlerType.Name} receives from unknown bus '{b}'."));
                errors.AddRange(MissingWireName(handler.MessageType));
            }

            foreach (MessageTypeRegistration messageType in messageTypes)
            {
                errors.AddRange(UnknownBuses(messageType.Buses).Select(b => $"{messageType.MessageType.Name} is received on unknown bus '{b}'."));
            }

            foreach (PendingSubscription subscription in subscriptions)
            {
                if (subscription.BusName == null && buses.Count != 1)
                {
                    errors.Add($"The subscription to '{subscription.Subscription.Exchange}' has no bus and this application has {buses.Count}. Pass the bus name.");
                }

                errors.AddRange(UnknownBuses(subscription.BusName == null ? new string[0] : new[] { subscription.BusName })
                    .Select(b => $"The subscription to '{subscription.Subscription.Exchange}' is for unknown bus '{b}'."));
            }

            foreach (BusRegistration bus in buses.Values)
            {
                // Two types with one wire name on the same bus would make incoming messages ambiguous.
                IEnumerable<string> duplicates = GetMessageRegistrations(bus.Name)
                    .GroupBy(r => r.WireName)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"Bus '{bus.Name}' has several message types named '{g.Key}': {string.Join(", ", g.Select(r => r.MessageType.Name))}.");
                errors.AddRange(duplicates);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Messaging is misconfigured:\n" + string.Join("\n", errors.Distinct()));
            }
        }

        private IEnumerable<string> UnknownBuses(IEnumerable<string> busNames)
        {
            return busNames.Where(b => !buses.ContainsKey(b));
        }

        private static IEnumerable<string> MissingWireName(Type messageType)
        {
            try
            {
                WireNames.Get(messageType);
                return Enumerable.Empty<string>();
            }
            catch (InvalidOperationException ex)
            {
                return new[] { ex.Message };
            }
        }
    }
}
