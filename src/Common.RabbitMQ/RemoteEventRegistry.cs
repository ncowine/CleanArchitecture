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
    /// <item>The constructor finds every <see cref="RemotePubSubEvent{TPayload}"/> (each needs <see cref="RemoteEventAttribute"/>);
    /// plain <c>PubSubEvent&lt;T&gt;</c> events are ignored. Unchanged from the first release.</item>
    /// <item><see cref="Add"/> finds every <c>PubSubEvent&lt;T&gt;</c> in an assembly, with no attribute or base class needed
    /// (ADR 0002, section 4): <c>new RemoteEventRegistry().Add(typeof(OrderSaved).Assembly)</c>.</item>
    /// </list>
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

        /// <summary>
        /// Registers every concrete <c>PubSubEvent&lt;T&gt;</c> in <paramref name="assembly"/> as able to travel between
        /// applications, found by its full type name. No attribute or special base class is needed; events that are only
        /// raised locally are registered too, harmlessly. An event already registered keeps its registration.
        /// </summary>
        /// <param name="busName">
        /// The bus these events travel on. Null leaves them unassigned: every bus of the application handles them, which is
        /// right for an application with one connection. A <see cref="RemoteEventAttribute"/> on an event still wins.
        /// </param>
        public RemoteEventRegistry Add(Assembly assembly, string busName = null)
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

                string bus = type.GetCustomAttribute<RemoteEventAttribute>(inherit: false)?.Bus ?? busName;
                RemoteEventDescriptor descriptor = new RemoteEventDescriptor(type, payloadType, bus);
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

        /// <summary>The events of <paramref name="busName"/>, plus unassigned ones (see <see cref="Add"/>).</summary>
        public IEnumerable<RemoteEventDescriptor> GetByBus(string busName)
        {
            return eventsByType.Values.Where(d => d.BusName == null || string.Equals(d.BusName, busName, StringComparison.OrdinalIgnoreCase));
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
