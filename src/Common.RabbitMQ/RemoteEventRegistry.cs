using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Knows which Prism events can travel between applications: every <see cref="RemotePubSubEvent{TPayload}"/>, and only
    /// those. It needs no registration: it joins when it is subscribed to or published, and is bound only when subscribed
    /// to. Plain <see cref="PubSubEvent{TPayload}"/> events are local.
    /// An event belongs to no bus: every bus receives it, and the publisher chooses where to send it.
    /// </summary>
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
                    AddName(descriptor);
                    eventsByType[type] = descriptor;
                }
            }
        }

        public IReadOnlyCollection<RemoteEventDescriptor> Events => eventsByType.Values.ToList();

        /// <summary>The event's descriptor, registering a <see cref="RemotePubSubEvent{TPayload}"/> the first time. Null for a local event.</summary>
        internal RemoteEventDescriptor Find(Type eventType)
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
            AddName(descriptor);
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

        /// <summary>Two events with one wire name would make incoming messages ambiguous, so the second throws.</summary>
        private void AddName(RemoteEventDescriptor descriptor)
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
