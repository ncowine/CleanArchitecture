using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Knows which Prism events can travel between applications.
    /// <list type="bullet">
    /// <item>The constructor finds every <see cref="RemotePubSubEvent{TPayload}"/>; plain <see cref="PubSubEvent{TPayload}"/>
    /// events are ignored.</item>
    /// <item><see cref="Add"/> finds every <see cref="PubSubEvent{TPayload}"/> in an assembly, with no special base class needed
    /// (ADR 0002, section 4): <c>new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly)</c>.</item>
    /// </list>
    /// An event belongs to no bus: every bus receives it, and the publisher chooses where to send it.
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

        public IReadOnlyCollection<RemoteEventDescriptor> Events => eventsByType.Values;

        /// <summary>
        /// Registers every concrete <see cref="PubSubEvent{TPayload}"/> in <paramref name="assembly"/> as able to travel between
        /// applications, found by its full type name. No special base class is needed; events that are only
        /// raised locally are registered too, harmlessly. An event already registered keeps its registration.
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
