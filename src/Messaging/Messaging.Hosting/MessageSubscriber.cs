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
    internal sealed class MessageSubscriber : IMessageSubscriber, IInboundDispatcher
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

        public IDisposable Subscribe<TMessage>(Action<TMessage> handler, SubscribeOptions options = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return AddSubscription(typeof(TMessage), options, (message, context, token) =>
            {
                handler((TMessage)message);
                return Task.CompletedTask;
            });
        }

        public IDisposable Subscribe<TMessage>(Func<TMessage, MessageContext, CancellationToken, Task> handler, SubscribeOptions options = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return AddSubscription(typeof(TMessage), options, (message, context, token) => handler((TMessage)message, context, token));
        }

        public async Task DispatchAsync(object message, MessageContext context, CancellationToken cancellationToken)
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
            string wireName = WireNames.GetWireName(messageType);

            if (wireNameOwners.TryGetValue(wireName, out Type other) && other != messageType)
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
                provider.GetRequiredKeyedService<RabbitMQBus>(busName).StartReceiving(registration);
            }
        }

        private void StopReceivingIfUnused(Type messageType)
        {
            if (subscriptions.ContainsKey(messageType) || !receiving.Remove(messageType))
            {
                return;
            }

            string wireName = WireNames.GetWireName(messageType);
            foreach (string busName in registry.BusNames)
            {
                provider.GetRequiredKeyedService<RabbitMQBus>(busName).StopReceiving(wireName);
            }
        }

        private IDisposable AddSubscription(Type messageType, SubscribeOptions options, Func<object, MessageContext, CancellationToken, Task> invoke)
        {
            ThrowIfNoRoutingKey(options);
            RabbitMQBus bindingBus = options == null ? null : GetBusForRoutingKey(provider, registry, options.Bus, options.Exchange);
            MessageSubscription subscription = new MessageSubscription(messageType, options, bindingBus, invoke);
            lock (gate)
            {
                StartReceiving(subscription.MessageType);
                try
                {
                    bindingBus?.BindRoutingKey(options.Exchange, options.RoutingKey);
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

            return new SubscriptionToken(() => RemoveSubscription(subscription));
        }

        private void RemoveSubscription(MessageSubscription subscription)
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
                subscription.BindingBus?.UnbindRoutingKey(subscription.Options.Exchange, subscription.Options.RoutingKey);
                StopReceivingIfUnused(subscription.MessageType);
            }
        }

        private static void ThrowIfNoRoutingKey(SubscribeOptions options)
        {
            if (options != null && string.IsNullOrWhiteSpace(options.RoutingKey))
            {
                throw new ArgumentException("A routing key is required.", nameof(options));
            }
        }

        internal static RabbitMQBus GetBusForRoutingKey(IServiceProvider provider, MessagingRegistry registry, string busName, string exchange)
        {
            string selected = BusSelection.GetBusNameForRoutingKey(
                registry.BusNames,
                (name, ex) => provider.GetRequiredKeyedService<RabbitMQBus>(name).UsesExchange(ex),
                registry.DefaultBus,
                busName,
                exchange);
            return provider.GetRequiredKeyedService<RabbitMQBus>(selected);
        }
    }
}
