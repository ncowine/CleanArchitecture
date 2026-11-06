using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    internal sealed class MessagingRegistry
    {
        /// <summary>Bus name → its serializer; null means the System.Text.Json default.</summary>
        private readonly Dictionary<string, IMessageSerializer> buses = new Dictionary<string, IMessageSerializer>(StringComparer.Ordinal);
        private readonly List<HandlerRegistration> handlers = new List<HandlerRegistration>();
        private readonly List<MessageTypeRegistration> messageTypes = new List<MessageTypeRegistration>();
        private readonly List<PendingSubscription> subscriptions = new List<PendingSubscription>();

        public IReadOnlyCollection<string> BusNames => buses.Keys;

        public string DefaultBus { get; set; }

        public void AddBus(string name, IMessageSerializer serializer)
        {
            if (buses.ContainsKey(name))
            {
                throw new InvalidOperationException($"Bus '{name}' is already added.");
            }

            buses.Add(name, serializer);
        }

        public string GetDefaultBus()
        {
            return DefaultBus;
        }

        public IMessageSerializer GetSerializer(string busName)
        {
            return buses[busName];
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

        /// <summary>The routing keys handler classes ask for with <see cref="RoutingKeyAttribute"/>, and their buses (empty: every bus).</summary>
        public IEnumerable<(RoutingKeyAttribute Key, IReadOnlyCollection<string> Buses)> GetHandlerRoutingKeys()
        {
            return handlers
                .GroupBy(h => h.HandlerType)
                .SelectMany(g => g.Key.GetCustomAttributes<RoutingKeyAttribute>(inherit: false)
                    .Select(a => (a, (IReadOnlyCollection<string>)g.First().Buses)));
        }

        public IEnumerable<HandlerRegistration> GetHandlers(Type messageType, string busName)
        {
            return handlers.Where(h => h.MessageType == messageType && Receives(h.Buses, busName));
        }

        public IEnumerable<MessageRegistration> GetMessageRegistrations(string busName)
        {
            IEnumerable<Type> handled = handlers
                .Where(h => Receives(h.Buses, busName))
                .Select(h => h.MessageType);
            IEnumerable<Type> added = messageTypes
                .Where(m => Receives(m.Buses, busName))
                .Select(m => m.MessageType);

            return handled.Concat(added)
                .Distinct()
                .Select(t => new MessageRegistration(WireNames.Get(t), t));
        }

        /// <summary>Fails fast at startup instead of on the first message.</summary>
        public void Validate()
        {
            List<string> errors = new List<string>();

            if (DefaultBus == null && buses.Count > 0)
            {
                errors.Add("No default bus: call UseDefaultBus(configuration[\"Messaging:DefaultBus\"]) and set Messaging:DefaultBus.");
            }
            else if (DefaultBus != null && !buses.ContainsKey(DefaultBus))
            {
                errors.Add($"The default bus '{DefaultBus}' is not configured.");
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

            foreach (string busName in buses.Keys)
            {
                // Two types with one wire name on the same bus would make incoming messages ambiguous.
                IEnumerable<string> duplicates = GetMessageRegistrations(busName)
                    .GroupBy(r => r.WireName)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"Bus '{busName}' has several message types named '{g.Key}': {string.Join(", ", g.Select(r => r.MessageType.Name))}.");
                errors.AddRange(duplicates);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Messaging is misconfigured:\n" + string.Join("\n", errors.Distinct()));
            }
        }

        /// <summary>No buses means every bus.</summary>
        private static bool Receives(IReadOnlyCollection<string> buses, string busName)
        {
            return buses.Count == 0 || buses.Contains(busName, StringComparer.Ordinal);
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
