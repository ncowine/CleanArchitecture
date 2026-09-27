using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Prism.Events;

namespace Common.RabbitMQ
{
    /// <summary>
    /// Describes one remote event and knows how to raise it on an <see cref="IEventAggregator"/> via reflection.
    /// </summary>
    public sealed class RemoteEventDescriptor
    {
        private static readonly MethodInfo getEventDefinition =
            typeof(IEventAggregator).GetMethod(nameof(IEventAggregator.GetEvent));

        private readonly MethodInfo getEventMethod;
        private readonly MethodInfo publishMethod;

        public RemoteEventDescriptor(Type eventType, Type payloadType, string busName)
        {
            EventType = eventType;
            PayloadType = payloadType;
            BusName = busName;
            EventName = eventType.FullName;

            getEventMethod = getEventDefinition.MakeGenericMethod(eventType);
            publishMethod = eventType.GetMethod(nameof(PubSubEvent<object>.Publish), new[] { payloadType });
        }

        /// <summary>The event's full type name; used as routing key and event-type header.</summary>
        public string EventName { get; }

        public Type EventType { get; }

        public Type PayloadType { get; }

        /// <summary>Null for an event registered with <see cref="RemoteEventRegistry.Add"/> without a bus: it belongs to every bus.</summary>
        public string BusName { get; }

        /// <summary>
        /// Equivalent to <c>eventAggregator.GetEvent&lt;EventType&gt;().Publish(payload)</c>.
        /// </summary>
        public void PublishLocal(IEventAggregator eventAggregator, object payload)
        {
            try
            {
                object pubSubEvent = getEventMethod.Invoke(eventAggregator, null);
                publishMethod.Invoke(pubSubEvent, new[] { payload });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
