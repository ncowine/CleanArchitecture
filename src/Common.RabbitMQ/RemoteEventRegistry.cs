using System;
using System.Collections.Concurrent;

namespace Common.RabbitMQ
{
    internal sealed class RemoteEventRegistry
    {
        private readonly ConcurrentDictionary<Type, RemoteEventMetadata> eventsByType = new ConcurrentDictionary<Type, RemoteEventMetadata>();
        private readonly ConcurrentDictionary<string, RemoteEventMetadata> eventsByName = new ConcurrentDictionary<string, RemoteEventMetadata>(StringComparer.Ordinal);

        internal RemoteEventMetadata FindOrAdd(Type eventType)
        {
            if (eventsByType.TryGetValue(eventType, out RemoteEventMetadata metadata))
            {
                return metadata;
            }

            Type payloadType = FindPayloadType(eventType);
            if (payloadType == null)
            {
                return null;
            }

            metadata = new RemoteEventMetadata(eventType, payloadType);
            AddWireName(metadata);
            return eventsByType.GetOrAdd(eventType, metadata);
        }

        internal bool TryGet(string eventName, out RemoteEventMetadata metadata)
        {
            return eventsByName.TryGetValue(eventName, out metadata);
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

        private void AddWireName(RemoteEventMetadata metadata)
        {
            RemoteEventMetadata existing = eventsByName.GetOrAdd(metadata.EventName, metadata);
            if (existing.EventType != metadata.EventType)
            {
                throw new InvalidOperationException(
                    $"{existing.EventType.FullName} and {metadata.EventType.FullName} both have the wire name '{metadata.EventName}'. Give one a different [Message] name.");
            }
        }
    }
}
