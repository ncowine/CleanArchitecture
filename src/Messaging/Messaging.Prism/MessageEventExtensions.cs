using System;
using Prism.Ioc;

namespace Messaging.Prism
{
    /// <summary>
    /// Publishing to other applications. The publisher comes from Prism's <see cref="ContainerLocator"/>.
    /// <see cref="PublishOptions"/> chooses the bus and routing key for one call; without it the message goes to the default
    /// bus, routed with its wire name. A bus that isn't configured throws.
    /// </summary>
    public static class MessageEventExtensions
    {
        /// <summary>
        /// Publishes <paramref name="message"/> to other applications, then to local subscribers, like the legacy
        /// <c>PublishRemote</c>. The sender doesn't receive its own message back.
        /// </summary>
        public static void PublishRemote<TMessage>(this MessageEvent<TMessage> messageEvent, TMessage message, PublishOptions options = null)
        {
            Send(messageEvent, message, options);
            messageEvent.Publish(message);
        }

        /// <summary>As <see cref="PublishRemote{TMessage}"/>, but only to other applications: local subscribers are not called.</summary>
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
