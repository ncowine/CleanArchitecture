using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Events;

namespace Legacy.RabbitMQ
{
    /// <summary>
    /// Fact F1 and assumption A10: every concrete <see cref="PubSubEvent{TPayload}"/> in the given assemblies can travel
    /// between applications, found by its full type name. No attribute or special base class.
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
                    if (type.IsAbstract || type.ContainsGenericParameters)
                    {
                        continue;
                    }

                    Type payloadType = FindPayloadType(type);
                    if (payloadType == null)
                    {
                        continue;
                    }

                    RemoteEventDescriptor descriptor = new RemoteEventDescriptor(type, payloadType);
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

        private static Type FindPayloadType(Type eventType)
        {
            for (Type type = eventType; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PubSubEvent<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            return null;
        }
    }
}
