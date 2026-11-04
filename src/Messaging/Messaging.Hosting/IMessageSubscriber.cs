using System;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    /// <summary>
    /// Subscribes code to received messages at runtime, without handler classes or Prism: for example a WPF view model.
    /// <code><![CDATA[
    /// subscriber.Subscribe<EmployeeUpdated>(OnUpdated, SynchronizationContext.Current);   // runs on the UI thread
    /// ]]></code>
    /// Subscribing starts receiving the message type on every bus: its class needs <see cref="MessageAttribute"/>, and nothing
    /// else has to be registered. The binding is created in the background, so a message published right after
    /// subscribing can be missed. Dispose the returned object to unsubscribe; when the last subscriber for a type goes, its
    /// binding is removed, unless a handler or <see cref="MessagingBuilder.AddMessages"/> still wants that type. A durable
    /// shared queue keeps the binding, because other instances depend on it, so subscribe there at startup and stay subscribed:
    /// the broker can give a message to an instance with no subscriber left, which drops it.
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

        /// <summary>
        /// Starts receiving <paramref name="messageType"/> on every bus, with no subscription of its own: for an
        /// <see cref="IMessageSink"/> such as the event aggregator bridge. Calls are counted: each needs its own
        /// <see cref="StopReceiving"/>.
        /// </summary>
        void Receive(Type messageType);

        /// <summary>Undoes one <see cref="Receive"/>. The binding stays while anything else wants the type.</summary>
        void StopReceiving(Type messageType);
    }
}
