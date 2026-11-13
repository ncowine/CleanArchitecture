using System;
using System.Collections.Generic;

namespace Messaging.Hosting
{
    internal sealed class MessagingRegistry
    {
        private readonly HashSet<string> buses = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> BusNames => buses;

        public string DefaultBus { get; set; }

        public void AddBus(string name)
        {
            if (!buses.Add(name))
            {
                throw new InvalidOperationException($"Bus '{name}' is already added.");
            }
        }

        public void Validate()
        {
            if (DefaultBus == null && buses.Count > 0)
            {
                throw new InvalidOperationException("Messaging is misconfigured:\nNo default bus: set Messaging:DefaultBus in the configuration.");
            }

            if (DefaultBus != null && !buses.Contains(DefaultBus))
            {
                throw new InvalidOperationException($"Messaging is misconfigured:\nThe default bus '{DefaultBus}' is not configured.");
            }
        }
    }
}
