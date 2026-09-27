using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    /// <summary>
    /// Subscribes code to received messages at runtime, without handler classes or Prism: for example a WPF view model.
    /// <code>
    /// subscriber.Subscribe&lt;EmployeeUpdated&gt;(OnUpdated, SynchronizationContext.Current);   // runs on the UI thread
    /// </code>
    /// A message type must be received for its subscribers to be called: add it with
    /// <see cref="MessagingBuilder.AddMessages"/> or a handler. Dispose the returned object to unsubscribe.
    /// Subscriptions are held strongly, so a subscriber that goes away must dispose them.
    /// </summary>
    public interface IMessageSubscriber
    {
        /// <summary>
        /// Calls <paramref name="handler"/> for every received <typeparamref name="TMessage"/>.
        /// With <paramref name="synchronizationContext"/> (such as the UI thread's) the call is posted to it: it runs later,
        /// and its exceptions go to that context (in WPF, the dispatcher), not to the message. Without one, it runs on the
        /// receiving thread and an exception fails the message (retried and dead-lettered on shared queues).
        /// </summary>
        IDisposable Subscribe<TMessage>(Action<TMessage> handler, SynchronizationContext synchronizationContext = null);

        /// <summary>
        /// Awaits <paramref name="handler"/> for every received <typeparamref name="TMessage"/> on the receiving thread.
        /// An exception fails the message.
        /// </summary>
        IDisposable Subscribe<TMessage>(Func<TMessage, MessageContext, CancellationToken, Task> handler);
    }
}
