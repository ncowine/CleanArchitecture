using System;

namespace Messaging.RabbitMQ
{
    /// <summary>The default observer: does nothing, adds no headers.</summary>
    internal sealed class NullMessagingObserver : IMessagingObserver
    {
        public static readonly NullMessagingObserver Instance = new NullMessagingObserver();

        public void OnPublishing(PublishContext context)
        {
        }

        public void OnPublished(PublishContext context)
        {
        }

        public void OnPublishFailed(PublishContext context, Exception exception)
        {
        }

        public void OnReceived(MessageContext context)
        {
        }

        public void OnHandled(MessageContext context, TimeSpan duration)
        {
        }

        public void OnHandlingFailed(MessageContext context, TimeSpan duration, Exception exception, FailedMessageAction action)
        {
        }

        public void OnConnectionChanged(ConnectionStateChange change)
        {
        }
    }
}
