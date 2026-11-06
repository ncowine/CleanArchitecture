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
    /// else has to be registered. Messages sent without a routing key already arrive, so subscribing only stops the type
    /// being dropped on arrival. With <see cref="SubscribeOptions"/> it also binds a routing key on the default bus:
    /// <code><![CDATA[
    /// subscriber.Subscribe<EquipmentDisposed>(OnDisposed, options: new SubscribeOptions { Exchange = "Poc.Net472", RoutingKey = "equipment" });
    /// ]]></code>
    /// Dispose the returned object to unsubscribe; when the last subscriber for a type goes, its messages are dropped again,
    /// unless a handler or <see cref="MessagingBuilder.AddMessages"/> still wants that type, and when the last subscription
    /// with a routing key goes, its binding is removed (on a shared queue, for every instance). On a durable shared queue the broker can give a message to an instance with no subscriber left,
    /// which drops it, so subscribe there at startup and stay subscribed.
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
        /// <param name="options">A routing key to bind, and the exchange to bind it on. Null for none.</param>
        IDisposable Subscribe<TMessage>(Action<TMessage> handler, SynchronizationContext synchronizationContext = null, SubscribeOptions options = null);

        /// <summary>
        /// Awaits <paramref name="handler"/> for every received <typeparamref name="TMessage"/> on the receiving thread.
        /// An exception fails the message.
        /// </summary>
        /// <param name="options">A routing key to bind, and the exchange to bind it on. Null for none.</param>
        IDisposable Subscribe<TMessage>(Func<TMessage, MessageContext, CancellationToken, Task> handler, SubscribeOptions options = null);

        /// <summary>
        /// Starts receiving <paramref name="messageType"/> on every bus, with no subscription of its own: for an
        /// <see cref="IMessageSink"/> such as the event aggregator bridge. Calls are counted: each needs its own
        /// <see cref="StopReceiving"/>, with the same <paramref name="options"/>.
        /// </summary>
        /// <param name="options">A routing key to bind, and the exchange to bind it on. Null for none.</param>
        void Receive(Type messageType, SubscribeOptions options = null);

        /// <summary>Undoes one <see cref="Receive"/>. The type is still received while anything else wants it.</summary>
        void StopReceiving(Type messageType, SubscribeOptions options = null);
    }
}
