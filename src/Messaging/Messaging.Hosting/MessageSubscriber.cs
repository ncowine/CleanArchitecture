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
    internal sealed class MessageSubscriber : IMessageSubscriber, IMessageSink
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;
        private readonly object gate = new object();
        private readonly Dictionary<string, Type> wireNameOwners = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly HashSet<Type> receiving = new HashSet<Type>();
        private Dictionary<Type, MessageSubscription[]> subscriptions = new Dictionary<Type, MessageSubscription[]>();

        public MessageSubscriber(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
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

        public async Task DeliverAsync(object message, MessageContext context, CancellationToken cancellationToken)
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

        private void StopReceivingIfUnused(Type messageType)
        {
            if (subscriptions.ContainsKey(messageType) || !receiving.Remove(messageType))
            {
                return;
            }

            string wireName = WireNames.Get(messageType);
            foreach (string busName in registry.BusNames)
            {
                if (!registry.GetMessageRegistrations(busName).Any(r => r.WireName == wireName))
                {
                    provider.GetRequiredKeyedService<RabbitMQBus>(busName).Unsubscribe(wireName);
                }
            }
        }

        private IDisposable Add(MessageSubscription subscription)
        {
            RequireRoutingKey(subscription.Options);
            SubscribeOptions options = subscription.Options;
            RabbitMQBus bindingBus = options == null ? null : SelectBindingBus(provider, registry, options.Bus, options.Exchange);
            lock (gate)
            {
                StartReceiving(subscription.MessageType);
                try
                {
                    bindingBus?.Bind(options.Exchange, options.RoutingKey);
                }
                catch
                {
                    try
                    {
                        StopReceivingIfUnused(subscription.MessageType);
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    throw;
                }

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
                StopReceivingIfUnused(subscription.MessageType);
            }
        }

        private static void RequireRoutingKey(SubscribeOptions options)
        {
            if (options != null && string.IsNullOrWhiteSpace(options.RoutingKey))
            {
                throw new ArgumentException("SubscribeOptions needs a RoutingKey.", nameof(options));
            }
        }

        private void Unbind(SubscribeOptions options)
        {
            if (options != null)
            {
                SelectBindingBus(provider, registry, options.Bus, options.Exchange).Unbind(options.Exchange, options.RoutingKey);
            }
        }

        internal static RabbitMQBus SelectBindingBus(IServiceProvider provider, MessagingRegistry registry, string busName, string exchange)
        {
            string selected = BusSelection.ForBinding(
                registry.BusNames,
                (name, ex) => provider.GetRequiredKeyedService<RabbitMQBus>(name).UsesExchange(ex),
                registry.DefaultBus,
                busName,
                exchange);
            return provider.GetRequiredKeyedService<RabbitMQBus>(selected);
        }
    }
}
