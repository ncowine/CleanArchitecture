using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Messaging.Hosting
{
    /// <summary>
    /// <see cref="IMessageSubscriber"/>, delivered as an <see cref="IMessageSink"/>: it sees every received message after its
    /// handlers and calls the subscriptions for that exact type, in the order they were made. Subscribing to a type also
    /// starts receiving it, so a queue is only bound to what the application listens for.
    /// </summary>
    internal sealed class MessageSubscriber : IMessageSubscriber, IMessageSink
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;
        private readonly object gate = new object();
        private readonly Dictionary<string, Type> receiving = new Dictionary<string, Type>(StringComparer.Ordinal);
        private Dictionary<Type, MessageSubscription[]> subscriptions = new Dictionary<Type, MessageSubscription[]>();

        public MessageSubscriber(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public void Receive(Type messageType)
        {
            if (messageType == null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            string wireName = WireNames.Get(messageType);

            lock (gate)
            {
                if (receiving.TryGetValue(wireName, out Type existing) && existing == messageType)
                {
                    return;
                }

                // Two types with one wire name would make incoming messages ambiguous, as MessagingRegistry.Validate says at startup.
                Type other = existing ?? registry.BusNames
                    .SelectMany(registry.GetMessageRegistrations)
                    .FirstOrDefault(r => r.WireName == wireName && r.MessageType != messageType)?.MessageType;
                if (other != null)
                {
                    throw new InvalidOperationException($"{messageType.Name} and {other.Name} both use the wire name '{wireName}'.");
                }

                receiving[wireName] = messageType;
            }

            MessageRegistration registration = new MessageRegistration(wireName, messageType);
            foreach (string busName in registry.BusNames)
            {
                // Binding talks to the broker, so it stays off the caller's thread, usually the UI thread.
                Task.Run(() => Bind(busName, registration));
            }
        }

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

        /// <summary>A failure is only logged: the bus keeps the subscription and binds it the next time it connects.</summary>
        private async Task Bind(string busName, MessageRegistration registration)
        {
            try
            {
                await provider.GetRequiredKeyedService<RabbitMQBus>(busName).Subscribe(registration).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException || ex is ObjectDisposedException))
            {
                provider.GetService<ILogger<MessageSubscriber>>()?.LogError(ex, "[{Bus}] Could not bind {WireName}.", busName, registration.WireName);
            }
            catch (Exception)
            {
                // The application is shutting down.
            }
        }

        /// <summary>Copy-on-write, so delivery never takes the lock.</summary>
        private IDisposable Add(MessageSubscription subscription)
        {
            Receive(subscription.MessageType);

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
