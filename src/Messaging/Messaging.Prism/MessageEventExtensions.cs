using System;
using Prism.Ioc;

namespace Messaging.Prism
{
    public static class MessageEventExtensions
    {
        /// <summary>
        /// Publishes <paramref name="message"/> to other applications, then to local subscribers, like the legacy
        /// <c>PublishRemote</c>. The sender doesn't receive its own message back.
        /// </summary>
        /// <param name="publisher">Optional. When null it is resolved from Prism's <see cref="ContainerLocator"/>.</param>
        public static void PublishRemote<TMessage>(this MessageEvent<TMessage> messageEvent, TMessage message, IMessagePublisher publisher = null)
        {
            Send(messageEvent, message, null, publisher);
            messageEvent.Publish(message);
        }

        /// <summary>As <see cref="PublishRemote{TMessage}"/>, routed with <paramref name="routingKey"/> (ADR 0002, section 3).</summary>
        public static void PublishRemoteTo<TMessage>(this MessageEvent<TMessage> messageEvent, string routingKey, TMessage message, IMessagePublisher publisher = null)
        {
            Send(messageEvent, message, RoutedTo(routingKey), publisher);
            messageEvent.Publish(message);
        }

        /// <summary>As <see cref="PublishRemote{TMessage}"/>, but only to other applications: local subscribers are not called.</summary>
        public static void PublishRemoteOnly<TMessage>(this MessageEvent<TMessage> messageEvent, TMessage message, IMessagePublisher publisher = null)
        {
            Send(messageEvent, message, null, publisher);
        }

        /// <summary>As <see cref="PublishRemoteTo{TMessage}"/>, but only to other applications: local subscribers are not called.</summary>
        public static void PublishRemoteOnlyTo<TMessage>(this MessageEvent<TMessage> messageEvent, string routingKey, TMessage message, IMessagePublisher publisher = null)
        {
            Send(messageEvent, message, RoutedTo(routingKey), publisher);
        }

        private static PublishOptions RoutedTo(string routingKey)
        {
            if (string.IsNullOrWhiteSpace(routingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(routingKey));
            }

            return new PublishOptions { RoutingKey = routingKey };
        }

        private static void Send<TMessage>(MessageEvent<TMessage> messageEvent, TMessage message, PublishOptions options, IMessagePublisher publisher)
        {
            if (messageEvent == null)
            {
                throw new ArgumentNullException(nameof(messageEvent));
            }

            IMessagePublisher resolved = publisher
                ?? ContainerLocator.Container?.Resolve<IMessagePublisher>()
                ?? throw new InvalidOperationException("No IMessagePublisher available. Register MessagingClient.Publisher in the Prism container, or pass it in.");

            // Buffers the message; the broker gets it in the background.
            resolved.Publish(message, options);
        }
    }
}
