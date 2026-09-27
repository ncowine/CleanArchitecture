using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Messaging.Hosting
{
    /// <summary>
    /// <see cref="IMessageSubscriber"/>, delivered as an <see cref="IMessageSink"/>: it sees every received message after its
    /// handlers and calls the subscriptions for that exact type, in the order they were made.
    /// </summary>
    internal sealed class MessageSubscriber : IMessageSubscriber, IMessageSink
    {
        private readonly object gate = new object();
        private Dictionary<Type, MessageSubscription[]> subscriptions = new Dictionary<Type, MessageSubscription[]>();

        public IDisposable Subscribe<TMessage>(Action<TMessage> handler, SynchronizationContext synchronizationContext = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (synchronizationContext == null)
            {
                return Add(new MessageSubscription(typeof(TMessage), (message, context, token) =>
                {
                    handler((TMessage)message);
                    return Task.CompletedTask;
                }));
            }

            return Add(new MessageSubscription(typeof(TMessage), (message, context, token) =>
            {
                synchronizationContext.Post(state => handler((TMessage)state), message);
                return Task.CompletedTask;
            }));
        }

        public IDisposable Subscribe<TMessage>(Func<TMessage, MessageContext, CancellationToken, Task> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return Add(new MessageSubscription(typeof(TMessage), (message, context, token) => handler((TMessage)message, context, token)));
        }

        public async Task Deliver(object message, MessageContext context, CancellationToken cancellationToken)
        {
            MessageSubscription[] current;
            if (!Volatile.Read(ref subscriptions).TryGetValue(message.GetType(), out current))
            {
                return;
            }

            foreach (MessageSubscription subscription in current)
            {
                await subscription.Invoke(message, context, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Copy-on-write, so delivery never takes the lock.</summary>
        private IDisposable Add(MessageSubscription subscription)
        {
            lock (gate)
            {
                Dictionary<Type, MessageSubscription[]> next = new Dictionary<Type, MessageSubscription[]>(subscriptions);
                next[subscription.MessageType] = next.TryGetValue(subscription.MessageType, out MessageSubscription[] existing)
                    ? existing.Concat(new[] { subscription }).ToArray()
                    : new[] { subscription };
                Volatile.Write(ref subscriptions, next);
            }

            return new SubscriptionToken(() => Remove(subscription));
        }

        private void Remove(MessageSubscription subscription)
        {
            lock (gate)
            {
                if (!subscriptions.TryGetValue(subscription.MessageType, out MessageSubscription[] existing))
                {
                    return;
                }

                Dictionary<Type, MessageSubscription[]> next = new Dictionary<Type, MessageSubscription[]>(subscriptions);
                MessageSubscription[] remaining = existing.Where(s => !ReferenceEquals(s, subscription)).ToArray();
                if (remaining.Length == 0)
                {
                    next.Remove(subscription.MessageType);
                }
                else
                {
                    next[subscription.MessageType] = remaining;
                }

                Volatile.Write(ref subscriptions, next);
            }
        }
    }
}
