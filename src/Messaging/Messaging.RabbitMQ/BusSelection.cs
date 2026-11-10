using System;
using System.Collections.Generic;
using System.Linq;

namespace Messaging.RabbitMQ
{
    internal static class BusSelection
    {
        public static string ForBinding(IEnumerable<string> busNames, Func<string, string, bool> usesExchange, string defaultBus, string bus, string exchange)
        {
            bool hasExchange = !string.IsNullOrWhiteSpace(exchange);

            if (!string.IsNullOrWhiteSpace(bus))
            {
                if (!busNames.Contains(bus, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Cannot bind a routing key on bus '{bus}': it is not configured for this application.");
                }

                if (hasExchange && !usesExchange(bus, exchange))
                {
                    throw NotUsedBy(bus, exchange);
                }

                return bus;
            }

            if (hasExchange)
            {
                List<string> matches = busNames.Where(name => usesExchange(name, exchange)).ToList();
                if (matches.Count == 1)
                {
                    return matches[0];
                }

                if (matches.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Exchange '{exchange}' is on several buses ({string.Join(", ", matches)}): set SubscribeOptions.Bus to say which.");
                }

                throw NoBusUses(exchange);
            }

            return !string.IsNullOrWhiteSpace(defaultBus)
                ? defaultBus
                : throw new InvalidOperationException("A routing key is bound on the default bus, and there is none: set Messaging:DefaultBus in the configuration.");
        }

        public static InvalidOperationException NotUsedBy(string bus, string exchange)
        {
            return new InvalidOperationException(
                $"Cannot bind a routing key on exchange '{exchange}': bus '{bus}' doesn't use it. Add it to Messaging:Buses:{bus}:Subscriptions, or check the name.");
        }

        public static InvalidOperationException NoBusUses(string exchange)
        {
            return new InvalidOperationException(
                $"Cannot bind a routing key on exchange '{exchange}': no bus uses it. Add it to a bus's Subscriptions in Messaging:Buses, or check the name.");
        }
    }
}
