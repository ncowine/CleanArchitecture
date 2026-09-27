using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.RabbitMQ.Tests.Broker;
using Messaging;
using Messaging.RabbitMQ;

namespace Common.RabbitMQ.Tests.LegacyModel
{
    /// <summary>
    /// An application built directly on the core (<see cref="RabbitMQBus"/>) in the ADR 0002 topology: its own exchange,
    /// subscriptions to other applications' exchanges, and message types identified by wire name.
    /// Call <see cref="Listen{T}"/> for every message type it handles, then <see cref="Start"/>.
    /// </summary>
    public sealed class CoreApp : IInboundDispatcher, IDisposable
    {
        private readonly Dictionary<string, Action<object>> recorders = new Dictionary<string, Action<object>>(StringComparer.Ordinal);
        private readonly List<MessageRegistration> registrations = new List<MessageRegistration>();
        private readonly ConcurrentQueue<MessageContext> contexts = new ConcurrentQueue<MessageContext>();
        private readonly RabbitMQBusOptions options;
        private readonly IMessagingObserver observer;
        private RabbitMQBus bus;

        public CoreApp(BrokerSettings broker, string exchangeName, string clientName, Action<RabbitMQBusOptions> configure, IMessagingObserver observer)
        {
            options = new RabbitMQBusOptions
            {
                BusName = clientName,
                HostName = broker.HostName,
                Port = broker.Port,
                VirtualHost = broker.VirtualHost,
                UserName = broker.UserName,
                Password = broker.Password,
                ExchangeName = exchangeName,
                ClientName = clientName,
                ReconnectDelay = TimeSpan.FromSeconds(1),
                OutstandingPollInterval = TimeSpan.FromMilliseconds(20),
                RetryDelay = TimeSpan.FromMilliseconds(50),
            };
            configure?.Invoke(options);
            this.observer = observer;
        }

        public RabbitMQBus Bus => bus;

        public bool IsConnected => bus != null && bus.IsConsumerConnected && bus.IsPublisherConnected;

        /// <summary>The contexts of every message dispatched, in order.</summary>
        public IReadOnlyCollection<MessageContext> Contexts => contexts;

        public Received<T> Listen<T>(string wireName)
            where T : class
        {
            Received<T> received = new Received<T>();
            recorders[wireName] = message => received.Add((T)message);
            registrations.Add(new MessageRegistration(wireName, typeof(T)));
            return received;
        }

        public CoreApp Start()
        {
            bus = new RabbitMQBus(options, TestJsonSerializer.Instance, this, observer);
            bus.Start(registrations);
            return this;
        }

        /// <param name="routingKey">Null for the wire name.</param>
        public void Publish(string wireName, object message, string routingKey = null)
        {
            bus.Enqueue(wireName, message, routingKey);
        }

        public Task Dispatch(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken)
        {
            contexts.Enqueue(context);
            recorders[registration.WireName](message);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            bus?.Dispose();
        }
    }
}
