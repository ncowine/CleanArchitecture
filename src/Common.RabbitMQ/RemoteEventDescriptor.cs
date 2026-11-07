using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Messaging;
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

        public RemoteEventDescriptor(Type eventType, Type payloadType)
        {
            EventType = eventType;
            PayloadType = payloadType;
            EventName = eventType.GetCustomAttribute<MessageAttribute>(inherit: false)?.WireName ?? eventType.FullName;

            getEventMethod = getEventDefinition.MakeGenericMethod(eventType);
            publishMethod = eventType.GetMethod(nameof(PubSubEvent<object>.Publish), new[] { payloadType });
        }

        /// <summary>
        /// The wire name, sent in the event-type header: the event's <see cref="MessageAttribute"/> name, else its full type
        /// name. Set <c>[Message("OtherApp.Events.OrderSaved")]</c> to exchange an event with an app that declares its own
        /// class, instead of sharing an event assembly.
        /// </summary>
        public string EventName { get; }

        public Type EventType { get; }

        public Type PayloadType { get; }

        /// <summary>
        /// Same as getting the <see cref="EventType"/> event from <see cref="IEventAggregator.GetEvent{TEventType}"/> and publishing <paramref name="payload"/> on it.
        /// </summary>
        public void PublishLocal(IEventAggregator eventAggregator, object payload)
        {
            try
            {
                publishMethod.Invoke(GetEvent(eventAggregator), new[] { payload });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        internal object GetEvent(IEventAggregator eventAggregator)
        {
            return getEventMethod.Invoke(eventAggregator, null);
        }
    }
}
