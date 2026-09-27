using System;

namespace Messaging
{
    /// <summary>
    /// Watches a bus: publishing, receiving, handling and connections. The hook for tracing, metrics and logging
    /// (ADR 0001, section 5). A bus without an observer uses a no-op one.
    /// <para>
    /// Callbacks must be fast and must not block. An exception thrown by a callback is logged by the bus and
    /// otherwise ignored; it never affects delivery.
    /// </para>
    /// </summary>
    public interface IMessagingObserver
    {
        /// <summary>
        /// A message is being buffered for publishing. Called on the publishing caller's thread, so ambient context
        /// (the current <c>Activity</c>, a correlation ID) is available. Add headers to <see cref="PublishContext.Headers"/> here.
        /// </summary>
        void OnPublishing(PublishContext context);

        /// <summary>The broker confirmed the message. Called on the bus's background thread.</summary>
        void OnPublished(PublishContext context);

        /// <summary>Sending failed. The message stays buffered and is retried. Called on the bus's background thread.</summary>
        void OnPublishFailed(PublishContext context, Exception exception);

        /// <summary>A message arrived and is about to be deserialized and dispatched. Called on the consumer's thread.</summary>
        void OnReceived(MessageContext context);

        /// <summary>The dispatcher completed. The message is acknowledged next.</summary>
        void OnHandled(MessageContext context, TimeSpan duration);

        /// <summary>
        /// Deserializing or dispatching failed. <paramref name="action"/> says what happens next: another attempt, the
        /// dead-letter queue, or (per-instance queues) nothing.
        /// </summary>
        void OnHandlingFailed(MessageContext context, TimeSpan duration, Exception exception, FailedMessageAction action);

        void OnConnectionChanged(ConnectionStateChange change);
    }
}
