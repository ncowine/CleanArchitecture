using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Finds every <see cref="RemotePubSubEvent{TPayload}"/> in the given assemblies.
    /// Plain <c>PubSubEvent&lt;T&gt;</c> events are local and ignored.
    /// </summary>
    public sealed class RemoteEventRegistry
    {
        private readonly Dictionary<Type, RemoteEventDescriptor> eventsByType = new Dictionary<Type, RemoteEventDescriptor>();
        private readonly Dictionary<string, RemoteEventDescriptor> eventsByName = new Dictionary<string, RemoteEventDescriptor>(StringComparer.Ordinal);

        public RemoteEventRegistry(params Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies.Distinct())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    RemoteEventAttribute attribute = type.GetCustomAttribute<RemoteEventAttribute>(inherit: false);
                    Type payloadType = FindPayloadType(type);

                    if (payloadType == null)
                    {
                        if (attribute != null)
                        {
                            throw new InvalidOperationException(
                                $"{type.FullName} is marked [RemoteEvent] but does not derive from RemotePubSubEvent<T>.");
                        }

                        continue;
                    }

                    if (type.IsAbstract || type.ContainsGenericParameters)
                    {
                        continue;
                    }

                    if (attribute == null)
                    {
                        throw new InvalidOperationException(
                            $"{type.FullName} derives from RemotePubSubEvent<T> but has no [RemoteEvent(\"<bus>\")] attribute.");
                    }

                    RemoteEventDescriptor descriptor = new RemoteEventDescriptor(type, payloadType, attribute.Bus);
                    eventsByType[type] = descriptor;
                    eventsByName[descriptor.EventName] = descriptor;
                }
            }
        }

        public IReadOnlyCollection<RemoteEventDescriptor> Events => eventsByType.Values;

        public bool TryGet(Type eventType, out RemoteEventDescriptor descriptor)
        {
            return eventsByType.TryGetValue(eventType, out descriptor);
        }

        public bool TryGet(string eventName, out RemoteEventDescriptor descriptor)
        {
            return eventsByName.TryGetValue(eventName, out descriptor);
        }

        public IEnumerable<RemoteEventDescriptor> GetByBus(string busName)
        {
            return eventsByType.Values.Where(d => string.Equals(d.BusName, busName, StringComparison.OrdinalIgnoreCase));
        }

        private static Type FindPayloadType(Type eventType)
        {
            for (Type type = eventType; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RemotePubSubEvent<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            return null;
        }
    }
}
