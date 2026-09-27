using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Messaging.Hosting
{
    /// <summary>Reads <see cref="MessageAttribute"/>. New messages always declare their wire name (ADR 0001, section 2).</summary>
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
