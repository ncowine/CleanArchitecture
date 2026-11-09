using System;
using Prism.Ioc;

namespace Messaging.Prism
{
    public static class MessageEventExtensions
    {
        public static void PublishRemote<TMessage>(this MessageEvent<TMessage> messageEvent, TMessage message, PublishOptions options = null)
        {
            Send(messageEvent, message, options);
            messageEvent.Publish(message);
        }

        public static void PublishRemoteOnly<TMessage>(this MessageEvent<TMessage> messageEvent, TMessage message, PublishOptions options = null)
        {
            Send(messageEvent, message, options);
        }

        private static void Send<TMessage>(MessageEvent<TMessage> messageEvent, TMessage message, PublishOptions options)
        {
            if (messageEvent == null)
            {
                throw new ArgumentNullException(nameof(messageEvent));
            }

            IMessagePublisher publisher = ContainerLocator.Container?.Resolve<IMessagePublisher>()
                ?? throw new InvalidOperationException("No IMessagePublisher available. Register MessagingClient.Publisher in the Prism container.");

            publisher.Publish(message, options);
        }
    }
}
