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
    /// starts receiving it, and a subscription's routing key is bound on the default bus while it lasts.
    /// </summary>
    internal sealed class MessageSubscriber : IMessageSubscriber, IMessageSink
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;
        private readonly object gate = new object();
        private readonly Dictionary<string, Type> wireNameOwners = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly HashSet<Type> receiving = new HashSet<Type>();
        private readonly Dictionary<Type, int> requested = new Dictionary<Type, int>();
        private Dictionary<Type, MessageSubscription[]> subscriptions = new Dictionary<Type, MessageSubscription[]>();

        public MessageSubscriber(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public void Receive(Type messageType, SubscribeOptions options = null)
        {
            if (messageType == null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            RequireRoutingKey(options);
            lock (gate)
            {
                StartReceiving(messageType);
                requested.TryGetValue(messageType, out int count);
                requested[messageType] = count + 1;
                Bind(options);
            }
        }

        public void StopReceiving(Type messageType, SubscribeOptions options = null)
        {
            if (messageType == null)
            {
                throw new ArgumentNullException(nameof(messageType));
            }

            lock (gate)
            {
                if (!requested.TryGetValue(messageType, out int count))
                {
                    return;
                }

                Unbind(options);
                if (count > 1)
                {
                    requested[messageType] = count - 1;
                    return;
                }

                requested.Remove(messageType);
                StopIfUnused(messageType);
            }
        }

        public IDisposable Subscribe<TMessage>(Action<TMessage> handler, SynchronizationContext synchronizationContext = null, SubscribeOptions options = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (synchronizationContext == null)
            {
                return Add(new MessageSubscription(typeof(TMessage), options, (message, context, token) =>
                {
                    handler((TMessage)message);
                    return Task.CompletedTask;
                }));
            }

            return Add(new MessageSubscription(typeof(TMessage), options, (message, context, token) =>
            {
                synchronizationContext.Post(state => handler((TMessage)state), message);
                return Task.CompletedTask;
            }));
        }

        public IDisposable Subscribe<TMessage>(Func<TMessage, MessageContext, CancellationToken, Task> handler, SubscribeOptions options = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return Add(new MessageSubscription(typeof(TMessage), options, (message, context, token) => handler((TMessage)message, context, token)));
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

        private void StartReceiving(Type messageType)
        {
            string wireName = WireNames.Get(messageType);

            // Two types with one wire name would make incoming messages ambiguous, as MessagingRegistry.Validate says at
            // startup. The owner is never forgotten, so another type can't take the wire name later.
            Type other = wireNameOwners.TryGetValue(wireName, out Type owner)
                ? owner
                : registry.BusNames.SelectMany(registry.GetMessageRegistrations).FirstOrDefault(r => r.WireName == wireName)?.MessageType;
            if (other != null && other != messageType)
            {
                throw new InvalidOperationException($"{messageType.Name} and {other.Name} both use the wire name '{wireName}'.");
            }

            wireNameOwners[wireName] = messageType;

            if (!receiving.Add(messageType))
            {
                return;
            }

            MessageRegistration registration = new MessageRegistration(wireName, messageType);
            foreach (string busName in registry.BusNames)
            {
                provider.GetRequiredKeyedService<RabbitMQBus>(busName).Subscribe(registration);
            }
        }

        private void StopIfUnused(Type messageType)
        {
            if (requested.ContainsKey(messageType) || subscriptions.ContainsKey(messageType) || !receiving.Remove(messageType))
            {
                return;
            }

            string wireName = WireNames.Get(messageType);
            foreach (string busName in registry.BusNames)
            {
                // Handlers and AddMessages keep a type subscribed for the life of the application.
                if (!registry.GetMessageRegistrations(busName).Any(r => r.WireName == wireName))
                {
                    provider.GetRequiredKeyedService<RabbitMQBus>(busName).Unsubscribe(wireName);
                }
            }
        }

        /// <summary>Copy-on-write, so delivery never takes the lock.</summary>
        private IDisposable Add(MessageSubscription subscription)
        {
            RequireRoutingKey(subscription.Options);
            lock (gate)
            {
                StartReceiving(subscription.MessageType);
                Bind(subscription.Options);

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
                Unbind(subscription.Options);
                StopIfUnused(subscription.MessageType);
            }
        }

        private static void RequireRoutingKey(SubscribeOptions options)
        {
            if (options != null && string.IsNullOrWhiteSpace(options.RoutingKey))
            {
                throw new ArgumentException("SubscribeOptions needs a RoutingKey.", nameof(options));
            }
        }

        private void Bind(SubscribeOptions options)
        {
            if (options != null)
            {
                DefaultBus().Bind(options.Exchange, options.RoutingKey);
            }
        }

        private void Unbind(SubscribeOptions options)
        {
            if (options != null)
            {
                DefaultBus().Unbind(options.Exchange, options.RoutingKey);
            }
        }

        private RabbitMQBus DefaultBus()
        {
            string busName = registry.GetDefaultBus()
                ?? throw new InvalidOperationException("A routing key is bound on the default bus, and there is none. Call UseDefaultBus.");
            return provider.GetRequiredKeyedService<RabbitMQBus>(busName);
        }
    }
}
