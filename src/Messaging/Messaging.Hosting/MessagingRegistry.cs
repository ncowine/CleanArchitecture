using System;
using System.Collections.Generic;

namespace Messaging.Hosting
{
    /// <summary>The configured bus names and the default bus, checked by <see cref="MessagingServiceCollectionExtensions.AddMessaging"/>.</summary>
    internal sealed class MessagingRegistry
    {
        public MessagingRegistry(IEnumerable<string> busNames, string defaultBus)
        {
            BusNames = new HashSet<string>(busNames, StringComparer.Ordinal);
            DefaultBus = defaultBus;
        }

        public IReadOnlyCollection<string> BusNames { get; }

        public string DefaultBus { get; }
    }
}
