using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Messaging.Hosting;
using Microsoft.Extensions.Hosting;
using Prism.Events;

namespace Messaging.Prism
{
    /// <summary>
    /// Starts receiving a message type when something subscribes to its <see cref="MessageEvent{TMessage}"/>, and stops when
    /// the last subscriber goes. Static because Prism creates the events and gives them no link back to the application; a
    /// view model can also subscribe before the host starts, so subscribed events are remembered until it does.
    /// </summary>
    internal sealed class MessageEventReceiver : IHostedService
    {
        private static readonly object gate = new object();
        private static readonly HashSet<IReceivedEvent> subscribed = new HashSet<IReceivedEvent>();
        private static readonly List<MessageEventReceiver> running = new List<MessageEventReceiver>();
        private static readonly List<(IReceivedEvent Event, SubscribeOptions Options)> keyed = new List<(IReceivedEvent Event, SubscribeOptions Options)>();

        private readonly IMessageSubscriber subscriber;
        private readonly IEventAggregator eventAggregator;

        public MessageEventReceiver(IMessageSubscriber subscriber, IEventAggregator eventAggregator)
        {
            this.subscriber = subscriber;
            this.eventAggregator = eventAggregator;
        }

        /// <summary>A class without a wire name can't travel, so its event stays local.</summary>
        public static void Subscribed(IReceivedEvent receivedEvent)
        {
            if (receivedEvent.MessageType.GetCustomAttribute<MessageAttribute>(inherit: false) == null)
            {
                return;
            }

            lock (gate)
            {
                if (!subscribed.Add(receivedEvent))
                {
                    return;
                }

                try
                {
                    foreach (MessageEventReceiver receiver in running)
                    {
                        if (receivedEvent.BelongsTo(receiver.eventAggregator))
                        {
                            receiver.subscriber.Receive(receivedEvent.MessageType);
                        }
                    }
                }
                catch
                {
                    subscribed.Remove(receivedEvent);
                    throw;
                }
            }
        }

        /// <summary>Checked under the lock, so a subscribe at the same moment either keeps the message received or starts it again.</summary>
        public static void StopIfUnused(IReceivedEvent receivedEvent)
        {
            lock (gate)
            {
                if (!subscribed.Contains(receivedEvent) || receivedEvent.HasSubscribers)
                {
                    return;
                }

                subscribed.Remove(receivedEvent);

                foreach (MessageEventReceiver receiver in running)
                {
                    if (receivedEvent.BelongsTo(receiver.eventAggregator))
                    {
                        receiver.subscriber.StopReceiving(receivedEvent.MessageType);
                    }
                }
            }
        }

        /// <summary>Binds a subscription's routing key, now or when the host starts. Undone by <see cref="Unbind"/>.</summary>
        public static void Bind(IReceivedEvent receivedEvent, SubscribeOptions options)
        {
            lock (gate)
            {
                foreach (MessageEventReceiver receiver in running)
                {
                    if (receivedEvent.BelongsTo(receiver.eventAggregator))
                    {
                        receiver.subscriber.Receive(receivedEvent.MessageType, options);
                    }
                }

                keyed.Add((receivedEvent, options));
            }
        }

        public static void Unbind(IReceivedEvent receivedEvent, SubscribeOptions options)
        {
            lock (gate)
            {
                int index = keyed.FindIndex(k => ReferenceEquals(k.Event, receivedEvent) && ReferenceEquals(k.Options, options));
                if (index < 0)
                {
                    return;
                }

                keyed.RemoveAt(index);
                foreach (MessageEventReceiver receiver in running)
                {
                    if (receivedEvent.BelongsTo(receiver.eventAggregator))
                    {
                        receiver.subscriber.StopReceiving(receivedEvent.MessageType, options);
                    }
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                running.Add(this);
                foreach (IReceivedEvent receivedEvent in subscribed)
                {
                    if (receivedEvent.BelongsTo(eventAggregator))
                    {
                        subscriber.Receive(receivedEvent.MessageType);
                    }
                }

                foreach ((IReceivedEvent receivedEvent, SubscribeOptions options) in keyed)
                {
                    if (receivedEvent.BelongsTo(eventAggregator))
                    {
                        subscriber.Receive(receivedEvent.MessageType, options);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                running.Remove(this);
            }

            return Task.CompletedTask;
        }
    }
}
