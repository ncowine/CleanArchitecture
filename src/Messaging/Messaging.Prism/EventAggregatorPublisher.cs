using Prism.Events;

namespace Messaging.Prism
{
    internal static class EventAggregatorPublisher<TMessage>
    {
        public static void Publish(IEventAggregator eventAggregator, object message)
        {
            eventAggregator.GetEvent<MessageEvent<TMessage>>().Publish((TMessage)message);
        }
    }
}
