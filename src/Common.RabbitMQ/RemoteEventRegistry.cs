using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Events;

namespace Common.RabbitMQ
{
    public sealed class RemoteEventRegistry
    {
        private readonly ConcurrentDictionary<Type, RemoteEventDescriptor> eventsByType = new ConcurrentDictionary<Type, RemoteEventDescriptor>();
        private readonly ConcurrentDictionary<string, RemoteEventDescriptor> eventsByName = new ConcurrentDictionary<string, RemoteEventDescriptor>(StringComparer.Ordinal);

        public RemoteEventRegistry(params Assembly[] assemblies)
        {
            foreach (Assembly assembly in assemblies.Distinct())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    Type payloadType = FindPayloadType(type);

                    if (payloadType == null || type.IsAbstract || type.ContainsGenericParameters)
                    {
                        continue;
                    }

                    RemoteEventDescriptor descriptor = new RemoteEventDescriptor(type, payloadType);
                    AddWireName(descriptor);
                    eventsByType[type] = descriptor;
                }
            }
        }

        public IReadOnlyCollection<RemoteEventDescriptor> Events => eventsByType.Values.ToList();

        internal RemoteEventDescriptor FindOrAdd(Type eventType)
        {
            if (eventsByType.TryGetValue(eventType, out RemoteEventDescriptor descriptor))
            {
                return descriptor;
            }

            Type payloadType = FindPayloadType(eventType);
            if (payloadType == null)
            {
                return null;
            }

            descriptor = new RemoteEventDescriptor(eventType, payloadType);
            AddWireName(descriptor);
            return eventsByType.GetOrAdd(eventType, descriptor);
        }

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
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RemotePubSubEvent<>))
                {
                    return type.GetGenericArguments()[0];
                }
            }

            return null;
        }

        private void AddWireName(RemoteEventDescriptor descriptor)
        {
            RemoteEventDescriptor existing = eventsByName.GetOrAdd(descriptor.EventName, descriptor);
            if (existing.EventType != descriptor.EventType)
            {
                throw new InvalidOperationException(
                    $"{existing.EventType.FullName} and {descriptor.EventType.FullName} both have the wire name '{descriptor.EventName}'. Give one a different [Message] name.");
            }
        }
    }
}
