using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Messaging.Hosting
{
    public static class WireNames
    {
        private static readonly ConcurrentDictionary<Type, string> cache = new ConcurrentDictionary<Type, string>();

        public static string Get(Type messageType)
        {
            if (messageType == null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            return cache.GetOrAdd(messageType, type =>
                type.GetCustomAttribute<MessageAttribute>(inherit: false)?.WireName
                ?? throw new InvalidOperationException($"{type.FullName} has no [Message(\"<wire name>\")] attribute."));
        }
    }
}
