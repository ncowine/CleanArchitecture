using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Prism.Events;

namespace Legacy.RabbitMQ
{
    /// <summary>One event, and how to raise it on an <see cref="IEventAggregator"/> via reflection.</summary>
    public sealed class RemoteEventDescriptor
    {
        private static readonly MethodInfo getEventDefinition =
            typeof(IEventAggregator).GetMethod(nameof(IEventAggregator.GetEvent));

        private readonly MethodInfo getEventMethod;
        private readonly MethodInfo publishMethod;

        public RemoteEventDescriptor(Type eventType, Type payloadType)
        {
            EventType = eventType;
            PayloadType = payloadType;
            EventName = eventType.FullName;

            getEventMethod = getEventDefinition.MakeGenericMethod(eventType);
            publishMethod = eventType.GetMethod(nameof(PubSubEvent<object>.Publish), new[] { payloadType });
        }

        /// <summary>Assumptions A2 and A3: the full type name is the routing key and the event-type header.</summary>
        public string EventName { get; }

        public Type EventType { get; }

        public Type PayloadType { get; }

        /// <summary><c>eventAggregator.GetEvent&lt;EventType&gt;().Publish(payload)</c>.</summary>
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
