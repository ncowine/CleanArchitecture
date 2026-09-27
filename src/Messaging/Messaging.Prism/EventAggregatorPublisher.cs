using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>Raises <see cref="MessageEvent{TMessage}"/> without reflection on every message.</summary>
    internal static class EventAggregatorPublisher<TMessage>
    {
        public static void Publish(IEventAggregator eventAggregator, object message)
        {
            eventAggregator.GetEvent<MessageEvent<TMessage>>().Publish((TMessage)message);
        }
    }
}
