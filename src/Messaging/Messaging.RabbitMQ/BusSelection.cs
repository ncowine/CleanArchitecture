using System;
using System.Collections.Generic;
using System.Linq;

namespace Messaging.RabbitMQ
{
    internal static class BusSelection
    {
        public static string GetBusNameForRoutingKey(IEnumerable<string> busNames, Func<string, string, bool> usesExchange, string defaultBus, string bus, string exchange)
        {
            bool hasExchange = !string.IsNullOrWhiteSpace(exchange);

            if (!string.IsNullOrWhiteSpace(bus))
            {
                if (!busNames.Contains(bus, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Bus '{bus}' is not configured.");
                }

                if (hasExchange && !usesExchange(bus, exchange))
                {
                    throw ExchangeNotOnBusError(bus, exchange);
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
                    throw new InvalidOperationException($"Exchange '{exchange}' is on several buses: {string.Join(", ", matches)}.");
                }

                throw NoBusForExchangeError(exchange);
            }

            return !string.IsNullOrWhiteSpace(defaultBus)
                ? defaultBus
                : throw new InvalidOperationException("No Messaging:DefaultBus configured.");
        }

        public static InvalidOperationException ExchangeNotOnBusError(string bus, string exchange)
        {
            return new InvalidOperationException($"Bus '{bus}' does not subscribe to exchange '{exchange}'.");
        }

        public static InvalidOperationException NoBusForExchangeError(string exchange)
        {
            return new InvalidOperationException($"No bus subscribes to exchange '{exchange}'.");
        }
    }
}
