using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Messaging.Hosting;
using Microsoft.Extensions.Hosting;

namespace Messaging.Prism
{
    /// <summary>
    /// Starts receiving a message type when something subscribes to its <see cref="MessageEvent{TMessage}"/>. Static because
    /// Prism creates the events and gives them no link back to the application; a view model can also subscribe before the
    /// host starts, so subscribed types are remembered until it does.
    /// </summary>
    internal sealed class MessageEventReceiver : IHostedService
    {
        private static readonly object gate = new object();
        private static readonly HashSet<Type> subscribed = new HashSet<Type>();
        private static readonly List<IMessageSubscriber> running = new List<IMessageSubscriber>();

        private readonly IMessageSubscriber subscriber;

        public MessageEventReceiver(IMessageSubscriber subscriber)
        {
            this.subscriber = subscriber;
        }

        /// <summary>A class without a wire name can't travel, so its event stays local.</summary>
        public static void Subscribed(Type messageType)
        {
            if (messageType.GetCustomAttribute<MessageAttribute>(inherit: false) == null)
            {
                return;
            }

            lock (gate)
            {
                if (!subscribed.Add(messageType))
                {
                    return;
                }

                foreach (IMessageSubscriber receiver in running)
                {
                    receiver.Receive(messageType);
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                running.Add(subscriber);
                foreach (Type messageType in subscribed)
                {
                    subscriber.Receive(messageType);
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                running.Remove(subscriber);
            }

            return Task.CompletedTask;
        }
    }
}
