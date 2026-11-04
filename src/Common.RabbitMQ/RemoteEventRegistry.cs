using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Knows which Prism events can travel between applications.
    /// <list type="bullet">
    /// <item>A <see cref="RemotePubSubEvent{TPayload}"/> needs no registration: it joins when it is subscribed to or
    /// published, and is bound only when subscribed to. Plain <see cref="PubSubEvent{TPayload}"/> events are local.</item>
    /// <item><see cref="Add"/> is for event assemblies not yet moved to that base class (ADR 0002, section 4): every
    /// <see cref="PubSubEvent{TPayload}"/> in the assembly can travel, and is bound at startup whether subscribed to or not,
    /// because the library can't see a plain event's subscribers.</item>
    /// </list>
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
                    eventsByType[type] = descriptor;
                    eventsByName[descriptor.EventName] = descriptor;
                }
            }
        }

        public IReadOnlyCollection<RemoteEventDescriptor> Events => eventsByType.Values.ToList();

        /// <summary>Plain events: nothing tells the library they were subscribed to, so they are bound from the start.</summary>
        internal IEnumerable<RemoteEventDescriptor> BoundAtStart => eventsByType.Values.Where(d => FindPayloadType(d.EventType) == null);

        /// <summary>
        /// Registers every concrete <see cref="PubSubEvent{TPayload}"/> in <paramref name="assembly"/> as able to travel between
        /// applications, found by its full type name, and binds it at startup: events that are only raised locally get a
        /// binding too. An event already registered keeps its registration.
        /// </summary>
        public RemoteEventRegistry Add(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.ContainsGenericParameters || eventsByType.ContainsKey(type))
                {
                    continue;
                }

                Type payloadType = FindPubSubPayloadType(type);
                if (payloadType == null)
                {
                    continue;
                }

                RemoteEventDescriptor descriptor = new RemoteEventDescriptor(type, payloadType);
                eventsByType[type] = descriptor;
                eventsByName[descriptor.EventName] = descriptor;
            }

            return this;
        }

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

            descriptor = eventsByType.GetOrAdd(eventType, new RemoteEventDescriptor(eventType, payloadType));
            eventsByName[descriptor.EventName] = descriptor;
            return descriptor;
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

        private static Type FindPubSubPayloadType(Type eventType)
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
