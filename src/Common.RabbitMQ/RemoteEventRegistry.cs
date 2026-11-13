using System;
using System.Collections.Concurrent;

namespace Common.RabbitMQ
{
    /// <summary>
    /// The remote events an app has used, by type and by wire name. Events are added the first time they are published or
    /// subscribed to; one registry is shared by every bus of a router, so two events with one wire name are refused.
    /// </summary>
    internal sealed class RemoteEventRegistry
    {
        private readonly ConcurrentDictionary<Type, RemoteEventDescriptor> eventsByType = new ConcurrentDictionary<Type, RemoteEventDescriptor>();
        private readonly ConcurrentDictionary<string, RemoteEventDescriptor> eventsByName = new ConcurrentDictionary<string, RemoteEventDescriptor>(StringComparer.Ordinal);

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

        internal bool TryGet(string eventName, out RemoteEventDescriptor descriptor)
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
