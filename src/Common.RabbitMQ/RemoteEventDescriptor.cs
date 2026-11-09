using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Messaging;
using Messaging.RabbitMQ;
using Prism.Events;

namespace Common.RabbitMQ
{
    public sealed class RemoteEventDescriptor
    {
        private static readonly MethodInfo getEventDefinition =
            typeof(IEventAggregator).GetMethod(nameof(IEventAggregator.GetEvent));

        private readonly MethodInfo getEventMethod;
        private readonly MethodInfo publishMethod;

        public RemoteEventDescriptor(Type eventType, Type payloadType)
        {
            MessageSerializer.EnsureNoTuples(payloadType);

            EventType = eventType;
            PayloadType = payloadType;
            EventName = eventType.GetCustomAttribute<MessageAttribute>(inherit: false)?.WireName ?? eventType.FullName;

            getEventMethod = getEventDefinition.MakeGenericMethod(eventType);
            publishMethod = eventType.GetMethod(nameof(PubSubEvent<object>.Publish), new[] { payloadType });
        }

        public string EventName { get; }

        public Type EventType { get; }

        public Type PayloadType { get; }

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
