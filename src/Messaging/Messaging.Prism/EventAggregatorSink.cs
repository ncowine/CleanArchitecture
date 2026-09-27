using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Messaging.Hosting;
using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>
    /// Raises every received message as <see cref="MessageEvent{TMessage}"/> on the application's
    /// <see cref="IEventAggregator"/>. Subscribers choose their thread as usual: <c>ThreadOption.UIThread</c> marshals to
    /// the UI thread the aggregator was created on.
    /// </summary>
    internal sealed class EventAggregatorSink : IMessageSink
    {
        private static readonly ConcurrentDictionary<Type, Action<IEventAggregator, object>> publishers =
            new ConcurrentDictionary<Type, Action<IEventAggregator, object>>();

        private readonly IEventAggregator eventAggregator;

        public EventAggregatorSink(IEventAggregator eventAggregator)
        {
            this.eventAggregator = eventAggregator;
        }

        public Task Deliver(object message, MessageContext context, CancellationToken cancellationToken)
        {
            publishers.GetOrAdd(message.GetType(), CreatePublisher)(eventAggregator, message);
            return Task.CompletedTask;
        }

        private static Action<IEventAggregator, object> CreatePublisher(Type messageType)
        {
            MethodInfo publish = typeof(EventAggregatorPublisher<>).MakeGenericType(messageType).GetMethod(nameof(EventAggregatorPublisher<object>.Publish));
            return (Action<IEventAggregator, object>)publish.CreateDelegate(typeof(Action<IEventAggregator, object>));
        }
    }
}
